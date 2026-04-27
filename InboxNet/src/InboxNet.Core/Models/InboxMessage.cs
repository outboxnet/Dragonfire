namespace InboxNet.Models;

public class InboxMessage
{
    public Guid Id { get; set; }

    /// <summary>
    /// Identifies which provider delivered this webhook (e.g. "stripe", "github").
    /// Set by the <see cref="Interfaces.IWebhookProvider"/> during parse.
    /// </summary>
    public string ProviderKey { get; set; } = default!;

    /// <summary>
    /// The provider's canonical event name (e.g. "invoice.paid").
    /// Handlers are matched against this value.
    /// </summary>
    public string EventType { get; set; } = default!;

    /// <summary>The raw request body as received over the wire. Handlers deserialize from this.</summary>
    public string Payload { get; set; } = default!;

    /// <summary>SHA-256 (hex, lowercase) of the raw <see cref="Payload"/>.</summary>
    public string ContentSha256 { get; set; } = default!;

    /// <summary>
    /// The provider's own stable event identifier when available (e.g. Stripe <c>evt_...</c>,
    /// GitHub <c>X-GitHub-Delivery</c>). Used for dedup in preference to <see cref="ContentSha256"/>.
    /// </summary>
    public string? ProviderEventId { get; set; }

    /// <summary>
    /// Dedup key persisted as part of the unique index: equal to <see cref="ProviderEventId"/>
    /// when the provider supplies one, otherwise equal to <see cref="ContentSha256"/>.
    /// A second insert with the same <c>(ProviderKey, DedupKey)</c> is rejected by the DB
    /// and treated as a duplicate (silently acknowledged).
    /// </summary>
    public string DedupKey { get; set; } = default!;

    public InboxMessageStatus Status { get; set; }
    public int RetryCount { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public string? LockedBy { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }
    public string? LastError { get; set; }

    public string? CorrelationId { get; set; }
    public string? TraceId { get; set; }
    public Dictionary<string, string>? Headers { get; set; }

    /// <summary>
    /// Opaque partitioning keys. <see cref="EntityId"/> drives ordered dispatch so that
    /// events belonging to the same entity are processed in arrival order.
    /// </summary>
    public string? TenantId { get; set; }
    public string? EntityId { get; set; }
}
