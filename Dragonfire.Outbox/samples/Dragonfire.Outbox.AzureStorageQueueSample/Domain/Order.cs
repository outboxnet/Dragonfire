namespace Dragonfire.Outbox.AzureStorageQueueSample.Domain;

public sealed class Order
{
    public Guid Id { get; set; }
    public string CustomerId { get; set; } = default!;
    public decimal Total { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTimeOffset CreatedAt { get; set; }
}
