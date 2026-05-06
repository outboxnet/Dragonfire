using Dragonfire.Outbox.Models;
using Microsoft.AspNetCore.Mvc;

namespace Dragonfire.WebhookPlatform.SampleApp.ViewComponents;

/// <summary>
/// Renders one expandable outbox-message row with its delivery attempts. Encapsulating it as
/// a view component lets the Outgoing list and Overview page render the same chunk of HTML
/// — including the JSON pretty-printer and per-attempt status pills — from the same source.
/// </summary>
public sealed class OutboxRowViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(
        OutboxMessage message,
        IReadOnlyList<DeliveryAttempt> attempts,
        IReadOnlyDictionary<Guid, WebhookSubscription> subscriptionsById)
        => View(new OutboxRowModel(message, attempts, subscriptionsById));

    public sealed record OutboxRowModel(
        OutboxMessage Message,
        IReadOnlyList<DeliveryAttempt> Attempts,
        IReadOnlyDictionary<Guid, WebhookSubscription> SubscriptionsById);
}
