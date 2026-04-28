namespace Dragonfire.ApiClientGen.Schema;

public sealed class ClientIR
{
    /// <summary>Root namespace, e.g. <c>Acme.BillingClient</c>.</summary>
    public string Namespace { get; set; } = "";

    /// <summary>Class-name prefix, e.g. <c>Billing</c> → <c>BillingClient</c>, <c>IBillingClient</c>.</summary>
    public string ClientName { get; set; } = "";

    public string BaseUrl { get; set; } = "";

    /// <summary>Headers that appear on every request — emitted as defaults on <c>{Prefix}ClientOptions.CommonHeaders</c>.</summary>
    public List<HeaderIR> CommonHeaders { get; set; } = new();

    public List<TypeIR> Types { get; set; } = new();

    public List<OperationIR> Operations { get; set; } = new();

    public List<string> Warnings { get; set; } = new();
}
