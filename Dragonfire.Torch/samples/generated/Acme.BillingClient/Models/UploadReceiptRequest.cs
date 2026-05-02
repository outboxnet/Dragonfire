using System.Text.Json.Serialization;

namespace Acme.BillingClient.Models;

public sealed class UploadReceiptRequest
{
    [JsonPropertyName("tenantId")]
    public string TenantId { get; set; } = "";

    [JsonPropertyName("note")]
    public string Note { get; set; } = "";

    [JsonPropertyName("receipt")]
    public Stream? Receipt { get; set; }

}
