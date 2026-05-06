namespace Dragonfire.WebhookPlatform.SampleApp.Domain;

public sealed class Order
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = default!;
    public string CustomerId { get; set; } = default!;
    public decimal Total { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTimeOffset CreatedAt { get; set; }
}
