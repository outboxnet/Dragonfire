using SyncLib.Abstractions;

namespace SyncLib.Core;

/// <summary>
/// Executes a single sync run for one or all registered streams, applying
/// retry/back-off, circuit breaker, state recording and metrics. Use this from
/// hosts that drive scheduling externally — Azure Functions timer triggers,
/// console jobs, controllers, etc.
/// </summary>
public interface ISyncRunner
{
    /// <summary>Composite keys of all registered streams.</summary>
    IReadOnlyCollection<SyncStateKey> RegisteredStreams { get; }

    /// <summary>Run one stream's sync once.</summary>
    /// <exception cref="ArgumentException">No stream with this key is registered.</exception>
    Task RunAsync(SyncStateKey key, CancellationToken cancellationToken = default);

    /// <summary>Run every registered stream of the given provider once, sequentially.</summary>
    Task<SyncRunSummary> RunProviderAsync(string providerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Run every registered stream once, sequentially. Returns a summary of
    /// outcomes; an exception in one stream does not abort the others — failures
    /// are recorded and surfaced via the summary.
    /// </summary>
    Task<SyncRunSummary> RunAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>Aggregate outcome of <see cref="ISyncRunner.RunAllAsync"/>.</summary>
public sealed record SyncRunSummary(
    int TotalStreams,
    int Succeeded,
    int Failed,
    int Skipped,
    IReadOnlyDictionary<SyncStateKey, string> StreamErrors)
{
    /// <summary>True when every stream either succeeded or was skipped (no failures).</summary>
    public bool IsHealthy => Failed == 0;
}
