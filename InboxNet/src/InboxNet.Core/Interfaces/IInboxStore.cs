using InboxNet.Models;

namespace InboxNet.Interfaces;

/// <summary>
/// One scheduled retry, used by <see cref="IInboxStore.IncrementRetryBulkAsync"/>.
/// </summary>
public readonly record struct InboxRetrySchedule(Guid MessageId, DateTimeOffset NextRetryAt, string? Error);

public interface IInboxStore
{
    /// <summary>
    /// Inserts <paramref name="message"/>. Returns the ID of the persisted row; when a
    /// duplicate is detected on <c>(ProviderKey, DedupKey)</c> the implementation returns the
    /// existing row's ID and sets <paramref name="isDuplicate"/> to <c>true</c>.
    /// </summary>
    Task<(Guid Id, bool IsDuplicate)> InsertOrGetDuplicateAsync(
        InboxMessage message,
        CancellationToken ct = default);

    /// <summary>
    /// Locks the next batch of eligible messages for dispatch.
    /// <paramref name="skipIds"/> excludes specific IDs — used by the cold path to avoid
    /// racing against the hot path for rows currently in flight on this instance.
    /// </summary>
    Task<IReadOnlyList<InboxMessage>> LockNextBatchAsync(
        int batchSize,
        TimeSpan visibilityTimeout,
        string lockedBy,
        IReadOnlySet<Guid>? skipIds = null,
        CancellationToken ct = default);

    /// <summary>
    /// PK-seek UPDATE that locks a single message by ID. Returns null if the row does not
    /// exist, is already locked, is already processed, or is scheduled for a future retry.
    /// </summary>
    Task<InboxMessage?> TryLockByIdAsync(
        Guid messageId,
        TimeSpan visibilityTimeout,
        string lockedBy,
        CancellationToken ct = default);

    /// <summary>
    /// Locks every supplied id that is currently eligible (Pending, lock expired or absent,
    /// retry due) in a single round-trip. Returns the message rows that won the lock —
    /// some IDs may be missing if they were already locked, processed, or not yet due.
    /// Used by the hot path to coalesce burst arrivals into one acquisition call.
    /// </summary>
    Task<IReadOnlyList<InboxMessage>> LockByIdsAsync(
        IReadOnlyCollection<Guid> messageIds,
        TimeSpan visibilityTimeout,
        string lockedBy,
        CancellationToken ct = default);

    Task<bool> MarkAsProcessedAsync(Guid messageId, string lockedBy, CancellationToken ct = default);

    /// <summary>
    /// Bulk variant of <see cref="MarkAsProcessedAsync"/>. Marks every supplied id as
    /// <see cref="InboxMessageStatus.Processed"/> in a single round-trip, scoped by
    /// <paramref name="lockedBy"/> so a stale lock cannot complete someone else's work.
    /// Returns the number of rows updated — may be less than <paramref name="messageIds"/>
    /// when some locks expired during dispatch.
    /// </summary>
    Task<int> MarkAsProcessedBulkAsync(
        IReadOnlyCollection<Guid> messageIds,
        string lockedBy,
        CancellationToken ct = default);

    Task<bool> IncrementRetryAsync(
        Guid messageId,
        string lockedBy,
        DateTimeOffset nextRetryAt,
        string? error = null,
        CancellationToken ct = default);

    /// <summary>
    /// Bulk retry-scheduling variant of <see cref="IncrementRetryAsync"/>. Each schedule
    /// supplies its own <c>NextRetryAt</c> and <c>Error</c>; rows are scoped by
    /// <paramref name="lockedBy"/>. Returns the number of rows updated.
    /// </summary>
    Task<int> IncrementRetryBulkAsync(
        IReadOnlyCollection<InboxRetrySchedule> schedules,
        string lockedBy,
        CancellationToken ct = default);

    Task<bool> MarkAsDeadLetteredAsync(Guid messageId, string lockedBy, CancellationToken ct = default);

    /// <summary>
    /// Bulk dead-letter variant of <see cref="MarkAsDeadLetteredAsync"/>. Returns the number
    /// of rows updated; missing entries indicate locks lost to expiry.
    /// </summary>
    Task<int> MarkAsDeadLetteredBulkAsync(
        IReadOnlyCollection<Guid> messageIds,
        string lockedBy,
        CancellationToken ct = default);

    Task ReleaseExpiredLocksAsync(CancellationToken ct = default);

    Task<int> PurgeProcessedMessagesAsync(DateTimeOffset olderThan, CancellationToken ct = default);
}
