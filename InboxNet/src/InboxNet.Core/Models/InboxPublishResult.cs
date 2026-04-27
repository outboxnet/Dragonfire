namespace InboxNet.Models;

/// <summary>
/// Result of <see cref="Interfaces.IInboxPublisher.PublishAsync"/>.
/// <see cref="IsDuplicate"/> is <c>true</c> when the unique dedup index already contained a row
/// for <c>(ProviderKey, DedupKey)</c> — the second delivery is silently acknowledged and
/// <see cref="MessageId"/> points to the original inbox record.
/// </summary>
public sealed record InboxPublishResult(Guid MessageId, bool IsDuplicate);
