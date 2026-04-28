using System.Text.Json.Serialization;

namespace Acme.BillingClient.Models;

public sealed class CreateInvoiceRequest
{
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "";

}
