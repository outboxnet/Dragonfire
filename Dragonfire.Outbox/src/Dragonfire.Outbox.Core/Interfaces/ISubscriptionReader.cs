using Dragonfire.Outbox.Models;

namespace Dragonfire.Outbox.Interfaces;

/// <summary>
/// Read-only view of webhook subscriptions. Used by the processing pipeline.
/// Database-backed stores implement the full <see cref="ISubscriptionStore"/>;
/// config-driven stores implement only this interface.
/// </summary>
public interface ISubscriptionReader
{
    /// <summary>
    /// Returns active subscriptions that should receive <paramref name="message"/>.
    /// Implementations may use <c>message.TenantId</c> for per-tenant routing.
    /// </summary>
    Task<IReadOnlyList<WebhookSubscription>> GetForMessageAsync(OutboxMessage message, CancellationToken ct = default);

    /// <summary>Returns active subscriptions for the given event type.</summary>
    Task<IReadOnlyList<WebhookSubscription>> GetByEventTypeAsync(string eventType, CancellationToken ct = default);

    Task<WebhookSubscription?> GetByIdAsync(Guid id, CancellationToken ct = default);
}
