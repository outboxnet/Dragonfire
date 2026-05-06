using Dragonfire.Inbox.Models;
using Dragonfire.Outbox.Models;

namespace Dragonfire.WebhookPlatform.SampleApp.Models;

public sealed class OverviewViewModel
{
    public int OutboxTotal { get; init; }
    public int InboxTotal { get; init; }
    public int DeliveryAttempts { get; init; }
    public int Subscriptions { get; init; }
    public IReadOnlyList<OutboxMessage> RecentOutgoing { get; init; } = [];
    public IReadOnlyList<InboxMessage> RecentIncoming { get; init; } = [];
}
