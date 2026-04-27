namespace Dragonfire.Sync.Abstractions;

/// <summary>
/// Outcome of a single sync run, persisted by <see cref="ISyncStateStore"/>.
/// </summary>
public enum SyncStatus
{
    /// <summary>The run has not produced a definitive outcome yet.</summary>
    Unknown = 0,

    /// <summary>The run is currently executing.</summary>
    Running = 1,

    /// <summary>The run completed and persisted records (or zero records, with no error).</summary>
    Succeeded = 2,

    /// <summary>The run failed after exhausting retries.</summary>
    Failed = 3,

    /// <summary>The run was skipped because the provider's circuit breaker was open.</summary>
    Skipped = 4
}
