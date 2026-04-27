using InboxNet.Models;

namespace InboxNet.Interfaces;

/// <summary>
/// Persists a validated webhook into the inbox table with SHA-based duplicate detection.
/// </summary>
public interface IInboxPublisher
{
    /// <summary>
    /// Inserts a new <see cref="InboxMessage"/> for the given provider and signals the processor.
    /// If a row with the same <c>(ProviderKey, DedupKey)</c> already exists, returns the existing
    /// message ID with <see cref="InboxPublishResult.IsDuplicate"/> = <c>true</c> without a second
    /// insert.
    /// </summary>
    Task<InboxPublishResult> PublishAsync(
        string providerKey,
        WebhookParseResult parse,
        CancellationToken cancellationToken = default);
}
