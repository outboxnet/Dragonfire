using System.Text.Json.Serialization;

namespace Acme.BillingClient.Models;

public sealed class CreateTenantRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("plan")]
    public string Plan { get; set; } = "";

}
