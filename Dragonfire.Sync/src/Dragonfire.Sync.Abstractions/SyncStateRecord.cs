namespace Dragonfire.Sync.Abstractions;

/// <summary>
/// Snapshot of a single sync stream's most recent activity. Returned by
/// <see cref="ISyncStateReader"/> and persisted by <see cref="ISyncStateStore"/>.
/// </summary>
public sealed record SyncStateRecord
{
    /// <summary>Composite identity of the stream.</summary>
    public required SyncStateKey Key { get; init; }

    /// <summary>UTC time the most recent successful run started, if any.</summary>
    public DateTime? LastSuccessAt { get; init; }

    /// <summary>UTC time the most recent run (success or failure) completed.</summary>
    public DateTime? LastRunAt { get; init; }

    /// <summary>Outcome of the most recent run.</summary>
    public SyncStatus LastStatus { get; init; }

    /// <summary>Duration of the most recent run.</summary>
    public TimeSpan? LastDuration { get; init; }

    /// <summary>Records handed to the handler on the most recent successful run.</summary>
    public int? LastRecordCount { get; init; }

    /// <summary>Error message of the most recent failed run, if any.</summary>
    public string? LastError { get; init; }

    /// <summary>Total number of successful runs since the record was created.</summary>
    public long TotalSuccesses { get; init; }

    /// <summary>Total number of failed runs since the record was created.</summary>
    public long TotalFailures { get; init; }

    /// <summary>Number of consecutive failed runs (resets on success).</summary>
    public int ConsecutiveFailures { get; init; }
}
