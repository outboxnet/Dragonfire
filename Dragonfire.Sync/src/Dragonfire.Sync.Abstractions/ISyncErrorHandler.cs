namespace Dragonfire.Sync.Abstractions;

/// <summary>
/// Optional callback invoked when a sync run finishes (success or failure).
/// Useful for emitting alerts, custom metrics, or audit events.
/// </summary>
public interface ISyncErrorHandler
{
    /// <summary>Called once per failed attempt (including intermediate retries).</summary>
    Task OnSyncErrorAsync(SyncStateKey key, Exception exception, int retryCount, CancellationToken cancellationToken = default);

    /// <summary>Called once per successful run.</summary>
    Task OnSyncSuccessAsync(SyncStateKey key, int recordsSynced, TimeSpan duration, CancellationToken cancellationToken = default);
}
