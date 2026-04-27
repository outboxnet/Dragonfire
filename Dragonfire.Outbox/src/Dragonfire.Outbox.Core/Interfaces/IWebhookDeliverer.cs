using Dragonfire.Outbox.Models;

namespace Dragonfire.Outbox.Interfaces;

public interface IWebhookDeliverer
{
    /// <summary>
    /// Delivers <paramref name="message"/> to <paramref name="subscription"/>.
    /// </summary>
    /// <param name="message">The outbox message to deliver.</param>
    /// <param name="subscription">The target webhook subscription.</param>
    /// <param name="deliveryId">
    /// A stable delivery identifier for this specific attempt. When provided it is sent as
    /// <c>X-Outbox-Delivery-Id</c> so webhook consumers can use it as an idempotency key.
    /// Pass <c>null</c> to generate a random ID (backwards-compatible default).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<DeliveryResult> DeliverAsync(
        OutboxMessage message,
        WebhookSubscription subscription,
        Guid? deliveryId = null,
        CancellationToken ct = default);
}
