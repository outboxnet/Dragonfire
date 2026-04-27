namespace Dragonfire.Outbox.Interfaces;

public interface IOutboxProcessor
{
    /// <summary>
    /// Processes the next batch of outbox messages. Returns the number of messages
    /// that were picked up for processing (0 means the queue was idle).
    /// Used by the cold-path poll loop and Azure Functions timer trigger.
    /// <paramref name="skipIds"/> lists message IDs currently being processed by the
    /// hot path on this instance. The cold path passes this set so the SQL query
    /// excludes those rows entirely — avoiding a lock race for the same message
    /// within the same process. The DB lock gate still prevents cross-instance duplicates.
    /// </summary>
    Task<int> ProcessBatchAsync(CancellationToken ct = default, IReadOnlySet<Guid>? skipIds = null);

    /// <summary>
    /// Attempts to lock and process a single message by ID.
    /// Returns <c>true</c> if this instance successfully locked the message and
    /// started delivery. Returns <c>false</c> if the message was already locked by
    /// another instance, already delivered, or scheduled for a future retry.
    /// Used by the hot-path Channel consumer for sub-millisecond same-instance delivery.
    /// </summary>
    Task<bool> TryProcessByIdAsync(Guid messageId, CancellationToken ct = default);
}
