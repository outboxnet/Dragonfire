namespace InboxNet.Interfaces;

public interface IInboxProcessor
{
    /// <summary>
    /// Cold-path entry. Releases expired locks (throttled), then locks and dispatches the
    /// next eligible batch. Returns the number of messages dispatched.
    /// </summary>
    Task<int> ProcessBatchAsync(CancellationToken ct = default, IReadOnlySet<Guid>? skipIds = null);

    /// <summary>
    /// Hot-path single-id entry. Used when only one signal arrives in the coalescing window.
    /// </summary>
    Task<bool> TryProcessByIdAsync(Guid messageId, CancellationToken ct = default);

    /// <summary>
    /// Hot-path bulk entry. Locks every supplied id that is currently eligible and dispatches
    /// the resulting messages with the same parallelism as the cold path. Returns the number
    /// of messages dispatched (may be less than <paramref name="messageIds"/>.Count when
    /// some ids were already locked, processed, or not yet due).
    /// </summary>
    Task<int> TryProcessByIdsAsync(IReadOnlyCollection<Guid> messageIds, CancellationToken ct = default);
}
