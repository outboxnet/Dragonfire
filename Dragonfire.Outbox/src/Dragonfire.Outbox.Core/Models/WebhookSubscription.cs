namespace Dragonfire.Outbox.Models;

public class WebhookSubscription
{
    public Guid Id { get; set; }
    /// <summary>
    /// Null means this subscription applies to all tenants (global).
    /// A non-null value restricts delivery to messages with a matching TenantId.
    /// </summary>
    public string? TenantId { get; set; }
    public string EventType { get; set; } = default!;
    public string WebhookUrl { get; set; } = default!;
    public string Secret { get; set; } = default!;
    public bool IsActive { get; set; } = true;
    public int MaxRetries { get; set; } = 5;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Dictionary<string, string>? CustomHeaders { get; set; }
}
