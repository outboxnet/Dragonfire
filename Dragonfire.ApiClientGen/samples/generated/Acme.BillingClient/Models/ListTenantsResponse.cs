using System.Text.Json.Serialization;

namespace Acme.BillingClient.Models;

public sealed class ListTenantsResponse
{
    [JsonPropertyName("items")]
    public List<ListTenantsResponseItem> Items { get; set; } = new();

    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

}
