namespace SagaNet.Sample.Workflows;

public sealed class OrderData
{
    public string OrderId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string CustomerEmail { get; set; } = string.Empty;

    // State tracked across steps
    public bool InventoryReserved { get; set; }
    public bool PaymentProcessed { get; set; }
    public bool Shipped { get; set; }
    public string? PaymentTransactionId { get; set; }
}
