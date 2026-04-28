using System.Text.Json.Serialization;

namespace Acme.BillingClient.Models;

public sealed class ExchangeTokenRequest
{
    [JsonPropertyName("grant_type")]
    public string GrantType { get; set; } = "";

    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = "";

    [JsonPropertyName("client_secret")]
    public string ClientSecret { get; set; } = "";

    [JsonPropertyName("scope")]
    public string Scope { get; set; } = "";

}
