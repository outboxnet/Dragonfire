namespace Dragonfire.Sync.Abstractions;

/// <summary>
/// Read/write access to per-stream sync state. The orchestrator uses this to
/// look up the last successful sync time (for incremental fetches) and to record
/// the outcome of every run. State is keyed by <see cref="SyncStateKey"/> so a
/// single provider can have multiple independent streams (one per DTO type).
/// </summary>
/// <remarks>
/// Inherits <see cref="ISyncStateReader"/>; if you only need to query state from
/// a dashboard or controller, depend on the reader interface instead.
/// </remarks>
public interface ISyncStateStore : ISyncStateReader
{
    /// <summary>Mark a run as starting. Sets <see cref="SyncStatus.Running"/>.</summary>
    Task RecordRunStartedAsync(SyncStateKey key, DateTime startedAtUtc, CancellationToken cancellationToken = default);

    /// <summary>Mark a run as successfully completed.</summary>
    Task RecordSuccessAsync(SyncStateKey key, DateTime startedAtUtc, TimeSpan duration, int recordCount, CancellationToken cancellationToken = default);

    /// <summary>Mark a run as failed (after retries are exhausted).</summary>
    Task RecordFailureAsync(SyncStateKey key, DateTime startedAtUtc, TimeSpan duration, Exception exception, CancellationToken cancellationToken = default);

    /// <summary>Mark a run as skipped (e.g. because the circuit breaker was open).</summary>
    Task RecordSkippedAsync(SyncStateKey key, DateTime atUtc, string reason, CancellationToken cancellationToken = default);
}

/// <summary>
/// Read-only view of sync state. Inject this into controllers or dashboards
/// that should not be able to mutate sync state.
/// </summary>
public interface ISyncStateReader
{
    /// <summary>Get the most recent state for a single stream, or <c>null</c> if it has never run.</summary>
    Task<SyncStateRecord?> GetAsync(SyncStateKey key, CancellationToken cancellationToken = default);

    /// <summary>Get the most recent state for every known stream.</summary>
    Task<IReadOnlyCollection<SyncStateRecord>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Get the most recent state for every stream belonging to a single provider.</summary>
    Task<IReadOnlyCollection<SyncStateRecord>> GetByProviderAsync(string providerName, CancellationToken cancellationToken = default);
}
