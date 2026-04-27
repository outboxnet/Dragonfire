using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Dragonfire.Sync.Abstractions;

namespace Dragonfire.Sync.Core;

/// <summary>
/// Hosted service that drives <see cref="ISyncRunner"/> on each stream's
/// configured <see cref="Configuration.StreamConfiguration.SyncInterval"/>. Use
/// when Dragonfire.Sync runs inside a long-lived host (Worker Service, ASP.NET, …).
/// </summary>
/// <remarks>
/// For Azure Functions or any externally-scheduled host, use
/// <see cref="ISyncRunner"/> directly via <c>AddSyncRunner()</c> instead.
/// </remarks>
public sealed class SyncOrchestrator : BackgroundService, ISyncOrchestrator
{
    private readonly ISyncRunner _runner;
    private readonly IReadOnlyDictionary<SyncStateKey, SyncStreamRegistration> _streams;
    private readonly ILogger<SyncOrchestrator> _logger;

    /// <summary>DI constructor.</summary>
    public SyncOrchestrator(
        ISyncRunner runner,
        IEnumerable<SyncStreamRegistration> registrations,
        ILogger<SyncOrchestrator> logger)
    {
        _runner = runner;
        _streams = registrations.ToDictionary(r => r.Key);
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<SyncStateKey> RegisteredStreams => _runner.RegisteredStreams;

    /// <inheritdoc />
    public Task TriggerManualSyncAsync(SyncStateKey key, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Manual sync triggered for {StreamKey}", key);
        return _runner.RunAsync(key, cancellationToken);
    }

    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_streams.Count == 0)
        {
            _logger.LogInformation("SyncOrchestrator started with no registered streams.");
            return Task.CompletedTask;
        }

        var loops = _streams.Values.Select(stream => RunStreamLoopAsync(stream, stoppingToken));
        return Task.WhenAll(loops);
    }

    private async Task RunStreamLoopAsync(SyncStreamRegistration stream, CancellationToken stoppingToken)
    {
        var interval = stream.Configuration.SyncInterval;
        _logger.LogInformation("Starting sync loop for {StreamKey} every {Interval}", stream.Key, interval);

        try
        {
            await _runner.RunAsync(stream.Key, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Initial run for {StreamKey} ended with handled error.", stream.Key);
        }

        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await _runner.RunAsync(stream.Key, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Run loop for {StreamKey} continued past handled error.", stream.Key);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // graceful shutdown
        }
    }
}
