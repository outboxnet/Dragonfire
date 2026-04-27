using Dragonfire.Outbox.Models;

namespace Dragonfire.Outbox.Interfaces;

public interface IOutboxStore
{
    Task SaveMessageAsync(OutboxMessage message, CancellationToken ct = default);

    /// <summary>
    /// Locks the next batch of eligible messages for processing.
    /// <paramref name="skipIds"/> excludes specific message IDs from the SQL query —
    /// used by the cold path to avoid racing against the hot path for the same row
    /// within the same process instance. Pass <c>null</c> when not applicable (e.g. Azure Functions).
    /// </summary>
    Task<IReadOnlyList<OutboxMessage>> LockNextBatchAsync(
        int batchSize,
        TimeSpan visibilityTimeout,
        string lockedBy,
        IReadOnlySet<Guid>? skipIds = null,
        CancellationToken ct = default);

    /// <summary>
    /// Marks a message as delivered. Only succeeds if <paramref name="lockedBy"/>
    /// still owns the lock. Returns <c>false</c> if the lock was stolen.
    /// </summary>
    Task<bool> MarkAsProcessedAsync(Guid messageId, string lockedBy, CancellationToken ct = default);

    /// <summary>
    /// Marks a message as permanently failed. Only succeeds if <paramref name="lockedBy"/>
    /// still owns the lock. Returns <c>false</c> if the lock was stolen.
    /// </summary>
    Task<bool> MarkAsFailedAsync(Guid messageId, string lockedBy, string error, CancellationToken ct = default);

    /// <summary>
    /// Increments the retry count and schedules the next attempt. Only succeeds if
    /// <paramref name="lockedBy"/> still owns the lock. Returns <c>false</c> if the lock was stolen.
    /// </summary>
    Task<bool> IncrementRetryAsync(Guid messageId, string lockedBy, DateTimeOffset nextRetryAt, string? error = null, CancellationToken ct = default);

    /// <summary>
    /// Moves a message to the dead-letter state. Only succeeds if <paramref name="lockedBy"/>
    /// still owns the lock. Returns <c>false</c> if the lock was stolen.
    /// </summary>
    Task<bool> MarkAsDeadLetteredAsync(Guid messageId, string lockedBy, CancellationToken ct = default);

    /// <summary>
    /// Checks whether the given lock is still held for a message.
    /// Returns <c>true</c> if the message exists with <c>Status == Processing</c>,
    /// the matching <paramref name="lockedBy"/>, and a <c>LockedUntil</c> in the future.
    /// </summary>
    /// <summary>
    /// Attempts to atomically lock a single message by primary key.
    /// Returns the message if this instance won the lock, or <c>null</c> if the message
    /// does not exist, is already locked by another instance, is already delivered,
    /// or has a <c>NextRetryAt</c> in the future.
    /// This is a PK-seek UPDATE — far cheaper than <see cref="LockNextBatchAsync"/>.
    /// </summary>
    Task<OutboxMessage?> TryLockByIdAsync(Guid messageId, TimeSpan visibilityTimeout, string lockedBy, CancellationToken ct = default);

    Task<bool> IsLockHeldAsync(Guid messageId, string lockedBy, CancellationToken ct = default);

    Task ReleaseExpiredLocksAsync(CancellationToken ct = default);

    /// <summary>
    /// Deletes <see cref="MessageStatus.Delivered"/> and <see cref="MessageStatus.DeadLettered"/>
    /// messages whose <c>CreatedAt</c> is older than <paramref name="olderThan"/>.
    /// Returns the number of rows deleted.
    /// Call periodically (e.g. nightly) to prevent the OutboxMessages table from growing unbounded.
    /// </summary>
    Task<int> PurgeProcessedMessagesAsync(DateTimeOffset olderThan, CancellationToken ct = default);
}
