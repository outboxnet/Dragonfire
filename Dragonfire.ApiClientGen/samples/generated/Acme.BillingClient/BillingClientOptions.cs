namespace Acme.BillingClient;

public sealed class BillingClientOptions
{
    public string BaseUrl { get; set; } = "https://api.acme.test";

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Headers applied to every outgoing request. Pre-populated from headers
    /// observed on every request in the source Postman collection.
    /// </summary>
    public Dictionary<string, string> CommonHeaders { get; set; } = new()
    {
        { Constants.Headers.ApiVersion, "2024-01" },
    };
}
