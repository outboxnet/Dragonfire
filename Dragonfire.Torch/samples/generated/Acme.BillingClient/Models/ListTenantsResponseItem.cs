using System.Text.Json.Serialization;

namespace Acme.BillingClient.Models;

public sealed class ListTenantsResponseItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("plan")]
    public string Plan { get; set; } = "";

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

}
