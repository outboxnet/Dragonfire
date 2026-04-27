using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Dragonfire.Sync.Abstractions;
using Dragonfire.Sync.Core.Diagnostics;

namespace Dragonfire.Sync.Core;

/// <summary>
/// Default <see cref="ISyncRunner"/>. Holds per-stream circuit breakers and
/// applies retry/back-off, the state store, metrics and tracing. Singleton-safe.
/// </summary>
public sealed class SyncRunner : ISyncRunner
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SyncRunner> _logger;
    private readonly IReadOnlyDictionary<SyncStateKey, SyncStreamRegistration> _streams;
    private readonly ConcurrentDictionary<SyncStateKey, CircuitBreaker> _breakers = new();

    /// <summary>Constructor used by DI.</summary>
    public SyncRunner(
        IServiceProvider serviceProvider,
        IEnumerable<SyncStreamRegistration> registrations,
        ILogger<SyncRunner> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _streams = registrations.ToDictionary(r => r.Key);
    }

    /// <inheritdoc />
    public IReadOnlyCollection<SyncStateKey> RegisteredStreams => _streams.Keys.ToArray();

    /// <inheritdoc />
    public Task RunAsync(SyncStateKey key, CancellationToken cancellationToken = default)
    {
        if (!_streams.TryGetValue(key, out var stream))
        {
            throw new ArgumentException($"No sync stream registered with key '{key}'.", nameof(key));
        }
        return ExecuteOnceAsync(stream, cancellationToken);
    }

    /// <inheritdoc />
    public Task<SyncRunSummary> RunProviderAsync(string providerName, CancellationToken cancellationToken = default)
    {
        var streams = _streams.Values
            .Where(s => string.Equals(s.Key.ProviderName, providerName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return RunBatchAsync(streams, cancellationToken);
    }

    /// <inheritdoc />
    public Task<SyncRunSummary> RunAllAsync(CancellationToken cancellationToken = default) =>
        RunBatchAsync(_streams.Values.ToArray(), cancellationToken);

    private async Task<SyncRunSummary> RunBatchAsync(IReadOnlyCollection<SyncStreamRegistration> streams, CancellationToken cancellationToken)
    {
        if (streams.Count == 0)
        {
            return new SyncRunSummary(0, 0, 0, 0, new Dictionary<SyncStateKey, string>());
        }

        var errors = new Dictionary<SyncStateKey, string>();
        int succeeded = 0, failed = 0, skipped = 0;

        // Sequential by design: shared resources (DB, breakers) and predictable
        // log ordering matter more than wall-clock for typical sync workloads.
        // Callers that want parallelism can call RunAsync per stream themselves.
        foreach (var stream in streams)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stateStore = _serviceProvider.GetRequiredService<ISyncStateStore>();
            try
            {
                await ExecuteOnceAsync(stream, cancellationToken).ConfigureAwait(false);
                var state = await stateStore.GetAsync(stream.Key, cancellationToken).ConfigureAwait(false);
                switch (state?.LastStatus)
                {
                    case SyncStatus.Succeeded: succeeded++; break;
                    case SyncStatus.Skipped: skipped++; break;
                    case SyncStatus.Failed: failed++; if (state.LastError is { } e) errors[stream.Key] = e; break;
                    default: succeeded++; break;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                errors[stream.Key] = ex.Message;
                _logger.LogError(ex, "Unexpected error running stream {StreamKey}", stream.Key);
            }
        }

        return new SyncRunSummary(streams.Count, succeeded, failed, skipped, errors);
    }

    private async Task ExecuteOnceAsync(SyncStreamRegistration stream, CancellationToken cancellationToken)
    {
        var config = stream.Configuration;
        using var scope = _serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;
        var stateStore = sp.GetRequiredService<ISyncStateStore>();
        var errorHandler = sp.GetService<ISyncErrorHandler>();

        using var activity = SyncDiagnostics.ActivitySource.StartActivity("synclib.sync", ActivityKind.Internal);
        activity?.SetTag("sync.provider", stream.Key.ProviderName);
        activity?.SetTag("sync.stream", stream.Key.StreamName);

        var breaker = _breakers.GetOrAdd(stream.Key,
            _ => new CircuitBreaker(config.FailureThreshold, config.CircuitBreakerTimeout, _logger));

        var providerTag = new KeyValuePair<string, object?>("sync.provider", stream.Key.ProviderName);
        var streamTag = new KeyValuePair<string, object?>("sync.stream", stream.Key.StreamName);

        if (config.EnableCircuitBreaker && breaker.IsOpen)
        {
            _logger.LogWarning("Skipping sync for {StreamKey}: circuit breaker is open.", stream.Key);
            await stateStore.RecordSkippedAsync(stream.Key, DateTime.UtcNow, "Circuit breaker open", cancellationToken).ConfigureAwait(false);
            SyncDiagnostics.SyncSkipped.Add(1, providerTag, streamTag);
            activity?.SetTag("sync.status", "skipped");
            if (errorHandler is not null)
            {
                await errorHandler.OnSyncErrorAsync(stream.Key,
                    new CircuitBreakerOpenException("Circuit breaker is open"), 0, cancellationToken).ConfigureAwait(false);
            }
            return;
        }

        var startedAt = DateTime.UtcNow;
        await stateStore.RecordRunStartedAsync(stream.Key, startedAt, cancellationToken).ConfigureAwait(false);

        var stopwatch = Stopwatch.StartNew();
        Exception? lastError = null;

        for (var attempt = 0; attempt <= config.MaxRetryAttempts; attempt++)
        {
            try
            {
                var recordCount = await stream.ExecuteAsync(sp, cancellationToken).ConfigureAwait(false);
                stopwatch.Stop();

                breaker.RecordSuccess();
                await stateStore.RecordSuccessAsync(stream.Key, startedAt, stopwatch.Elapsed, recordCount, cancellationToken).ConfigureAwait(false);

                SyncDiagnostics.SyncDuration.Record(stopwatch.Elapsed.TotalMilliseconds, providerTag, streamTag);
                SyncDiagnostics.SyncSuccesses.Add(1, providerTag, streamTag);
                SyncDiagnostics.SyncRecords.Add(recordCount, providerTag, streamTag);

                activity?.SetTag("sync.records", recordCount);
                activity?.SetTag("sync.status", "succeeded");

                if (errorHandler is not null)
                {
                    await errorHandler.OnSyncSuccessAsync(stream.Key, recordCount, stopwatch.Elapsed, cancellationToken).ConfigureAwait(false);
                }

                _logger.LogInformation("Sync completed for {StreamKey}: {Count} records in {Duration}",
                    stream.Key, recordCount, stopwatch.Elapsed);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                if (errorHandler is not null)
                {
                    await errorHandler.OnSyncErrorAsync(stream.Key, ex, attempt + 1, cancellationToken).ConfigureAwait(false);
                }

                if (attempt < config.MaxRetryAttempts)
                {
                    var delay = TimeSpan.FromMilliseconds(config.RetryDelayBase.TotalMilliseconds * Math.Pow(2, attempt));
                    _logger.LogWarning(ex, "Sync failed for {StreamKey} (attempt {Attempt}/{MaxRetries}). Retrying in {Delay}.",
                        stream.Key, attempt + 1, config.MaxRetryAttempts, delay);
                    try
                    {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                }
            }
        }

        stopwatch.Stop();
        breaker.RecordFailure();
        await stateStore.RecordFailureAsync(stream.Key, startedAt, stopwatch.Elapsed, lastError!, cancellationToken).ConfigureAwait(false);

        SyncDiagnostics.SyncDuration.Record(stopwatch.Elapsed.TotalMilliseconds, providerTag, streamTag);
        SyncDiagnostics.SyncFailures.Add(1, providerTag, streamTag);

        activity?.SetTag("sync.status", "failed");
        activity?.SetStatus(ActivityStatusCode.Error, lastError?.Message);

        _logger.LogError(lastError, "Sync failed permanently for {StreamKey} after {Attempts} attempts.",
            stream.Key, config.MaxRetryAttempts + 1);
    }
}
