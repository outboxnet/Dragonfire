using System.Text.Json.Serialization;

namespace Acme.BillingClient.Models;

public sealed class ExchangeTokenResponse
{
    [JsonPropertyName("accessToken")]
    public string AccessToken { get; set; } = "";

    [JsonPropertyName("tokenType")]
    public string TokenType { get; set; } = "";

    [JsonPropertyName("expiresIn")]
    public int ExpiresIn { get; set; }

}
