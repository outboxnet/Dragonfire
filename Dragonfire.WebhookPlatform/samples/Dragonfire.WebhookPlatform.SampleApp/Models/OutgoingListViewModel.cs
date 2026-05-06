using Dragonfire.Outbox.Models;

namespace Dragonfire.WebhookPlatform.SampleApp.Models;

public sealed class OutgoingListViewModel
{
    public EventFilter Filter { get; init; } = new();
    public IReadOnlyList<OutboxMessage> Messages { get; init; } = [];
    public IReadOnlyDictionary<Guid, IReadOnlyList<DeliveryAttempt>> AttemptsByMessage { get; init; }
        = new Dictionary<Guid, IReadOnlyList<DeliveryAttempt>>();
    public IReadOnlyDictionary<Guid, WebhookSubscription> SubscriptionsById { get; init; }
        = new Dictionary<Guid, WebhookSubscription>();
    public IReadOnlyList<string> KnownEventTypes { get; init; } = [];
}
