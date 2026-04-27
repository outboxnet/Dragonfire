namespace Dragonfire.Outbox.Models;

public class DeliveryAttempt
{
    public Guid Id { get; set; }
    public Guid OutboxMessageId { get; set; }
    public Guid WebhookSubscriptionId { get; set; }
    public int AttemptNumber { get; set; }
    public DeliveryStatus Status { get; set; }
    public int? HttpStatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public string? ErrorMessage { get; set; }
    public long DurationMs { get; set; }
    public DateTimeOffset AttemptedAt { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }

    // Navigation to OutboxMessage only — no FK to WebhookSubscriptions because
    // config-driven subscriptions are never stored in that table.
    public OutboxMessage OutboxMessage { get; set; } = default!;
}
