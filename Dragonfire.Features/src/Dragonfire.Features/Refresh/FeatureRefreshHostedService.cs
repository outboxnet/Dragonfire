using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dragonfire.Features.Refresh;

/// <summary>
/// Periodically merges every registered <see cref="IFeatureSource"/> into the
/// <see cref="IFeatureStore"/>, then emits diff entries to <see cref="IFeatureAuditLog"/>.
///
/// <para>
/// Implementation note: this uses <see cref="PeriodicTimer"/> directly rather than the
/// <c>Dragonfire.Poller</c> library — Poller targets request/response polling with backoff,
/// not "tick every N seconds forever". Refreshing a feature snapshot is the latter.
/// </para>
///
/// <para>Source merging: when two sources advertise the same feature, the source registered
/// later in DI wins. Register configuration first, EF Core second, etc.</para>
/// </summary>
public sealed class FeatureRefreshHostedService : IHostedService, IAsyncDisposable
{
    private readonly IReadOnlyList<IFeatureSource> _sources;
    private readonly IFeatureStore _store;
    private readonly IFeatureAuditLog _auditLog;
    private readonly TimeProvider _timeProvider;
    private readonly FeatureRefreshOptions _options;
    private readonly ILogger<FeatureRefreshHostedService> _logger;

    private CancellationTokenSource? _stoppingCts;
    private Task? _loop;

    public FeatureRefreshHostedService(
        IEnumerable<IFeatureSource> sources,
        IFeatureStore store,
        IFeatureAuditLog auditLog,
        TimeProvider timeProvider,
        IOptions<FeatureRefreshOptions> options,
        ILogger<FeatureRefreshHostedService> logger)
    {
        _sources      = sources.ToArray();
        _store        = store;
        _auditLog     = auditLog;
        _timeProvider = timeProvider;
        _options      = options.Value;
        _logger       = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_sources.Count == 0)
        {
            _logger.LogWarning(
                "No IFeatureSource registered. Feature store will remain empty — every feature resolves to false.");
        }
        else if (_options.LoadOnStartup)
        {
            // Surface startup errors to the host: on a missing/broken source we want fail-fast,
            // not a silent app that has no flags.
            await RefreshOnceAsync(cancellationToken).ConfigureAwait(false);
        }

        _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = Task.Run(() => RunLoopAsync(_stoppingCts.Token), CancellationToken.None);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_stoppingCts is null) return;
        _stoppingCts.Cancel();

        if (_loop is not null)
        {
            try
            {
                // Wait for the loop to complete or the host's grace period to expire.
                await _loop.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { /* expected on timeout */ }
        }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_options.RefreshInterval, _timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await RefreshOnceAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (_options.ContinueOnSourceError)
                {
                    _logger.LogError(ex, "Feature refresh failed; keeping previous snapshot.");
                }
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    private async Task RefreshOnceAsync(CancellationToken cancellationToken)
    {
        // Later sources override earlier ones on name collision.
        var merged = new Dictionary<string, (FeatureDefinition Def, string Source)>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in _sources)
        {
            var loaded = await source.LoadAllAsync(cancellationToken).ConfigureAwait(false);
            foreach (var def in loaded)
                merged[def.Name] = (def, source.Name);
        }

        var diff = _store.Replace(merged.Values.Select(v => v.Def).ToArray());
        if (!diff.HasChanges) return;

        var entries = new List<FeatureAuditEntry>(diff.Added.Count + diff.Updated.Count + diff.Removed.Count);
        var now = _timeProvider.GetUtcNow();

        foreach (var def in diff.Added)
        {
            var src = merged.TryGetValue(def.Name, out var v) ? v.Source : "unknown";
            entries.Add(new FeatureAuditEntry(def.Name, FeatureAuditAction.Added, now, src,
                previousVersion: null, currentVersion: def.Version));
        }

        foreach (var (prev, current) in diff.Updated)
        {
            var src = merged.TryGetValue(current.Name, out var v) ? v.Source : "unknown";
            entries.Add(new FeatureAuditEntry(current.Name, FeatureAuditAction.Updated, now, src,
                previousVersion: prev.Version, currentVersion: current.Version));
        }

        foreach (var def in diff.Removed)
        {
            entries.Add(new FeatureAuditEntry(def.Name, FeatureAuditAction.Removed, now,
                source: "removed", previousVersion: def.Version, currentVersion: null));
        }

        _logger.LogInformation(
            "Feature refresh: +{Added} ~{Updated} -{Removed}",
            diff.Added.Count, diff.Updated.Count, diff.Removed.Count);

        await _auditLog.RecordAsync(entries, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_stoppingCts is null) return;
        _stoppingCts.Cancel();
        _stoppingCts.Dispose();
        if (_loop is not null)
        {
            try { await _loop.ConfigureAwait(false); }
            catch { /* swallow shutdown noise */ }
        }
    }
}
