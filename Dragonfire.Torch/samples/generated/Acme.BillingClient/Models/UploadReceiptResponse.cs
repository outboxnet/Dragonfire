using System.Text.Json.Serialization;

namespace Acme.BillingClient.Models;

public sealed class UploadReceiptResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("tenantId")]
    public string TenantId { get; set; } = "";

    [JsonPropertyName("filename")]
    public string Filename { get; set; } = "";

    [JsonPropertyName("sizeBytes")]
    public int SizeBytes { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

}
