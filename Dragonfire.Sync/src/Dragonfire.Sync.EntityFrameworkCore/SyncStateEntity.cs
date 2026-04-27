namespace Dragonfire.Sync.EntityFrameworkCore;

/// <summary>
/// EF-mapped persistence row for sync state. Public so consumers can include it
/// in their <see cref="Microsoft.EntityFrameworkCore.DbContext"/>; only the
/// <see cref="EfSyncStateStore{TContext}"/> should mutate instances. Composite
/// primary key: (<see cref="ProviderName"/>, <see cref="StreamName"/>).
/// </summary>
public sealed class SyncStateEntity
{
    /// <summary>Provider name; part of the composite primary key.</summary>
    public string ProviderName { get; set; } = null!;

    /// <summary>Stream name; part of the composite primary key.</summary>
    public string StreamName { get; set; } = null!;

    /// <summary>UTC time of the last successful run.</summary>
    public DateTime? LastSuccessAt { get; set; }

    /// <summary>UTC time of the most recent run completion.</summary>
    public DateTime? LastRunAt { get; set; }

    /// <summary>Most recent status (cast of <see cref="Dragonfire.Sync.Abstractions.SyncStatus"/>).</summary>
    public int LastStatus { get; set; }

    /// <summary>Most recent duration in <see cref="TimeSpan"/> ticks.</summary>
    public long? LastDurationTicks { get; set; }

    /// <summary>Records handed to the handler on the most recent successful run.</summary>
    public int? LastRecordCount { get; set; }

    /// <summary>Most recent error message, if any.</summary>
    public string? LastError { get; set; }

    /// <summary>Lifetime count of successful runs.</summary>
    public long TotalSuccesses { get; set; }

    /// <summary>Lifetime count of failed runs.</summary>
    public long TotalFailures { get; set; }

    /// <summary>Number of consecutive failed runs (resets on success).</summary>
    public int ConsecutiveFailures { get; set; }

    /// <summary>EF concurrency token.</summary>
    public byte[]? RowVersion { get; set; }
}
