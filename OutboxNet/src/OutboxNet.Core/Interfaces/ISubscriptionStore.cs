using OutboxNet.Models;

namespace OutboxNet.Interfaces;

/// <summary>
/// Full subscription store with read + write operations.
/// Extends <see cref="ISubscriptionReader"/> with mutation methods.
/// Database-backed stores implement this; config-driven stores implement only <see cref="ISubscriptionReader"/>.
/// </summary>
public interface ISubscriptionStore : ISubscriptionReader
{
    Task<WebhookSubscription> AddAsync(WebhookSubscription subscription, CancellationToken ct = default);
    Task UpdateAsync(WebhookSubscription subscription, CancellationToken ct = default);
    Task DeactivateAsync(Guid id, CancellationToken ct = default);
}
