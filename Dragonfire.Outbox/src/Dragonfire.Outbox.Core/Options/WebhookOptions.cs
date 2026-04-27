namespace Dragonfire.Outbox.Options;

public class WebhookOptions
{
    public const string SectionName = "Outbox:Webhooks";

    /// <summary>
    /// <list type="bullet">
    ///   <item><term>Global</term><description>All messages, regardless of TenantId, are delivered to the single <see cref="Global"/> endpoint.</description></item>
    ///   <item><term>PerTenant</term><description>Each message is routed to the endpoint registered for its <c>TenantId</c> in <see cref="Tenants"/>.</description></item>
    /// </list>
    /// </summary>
    public WebhookMode Mode { get; set; } = WebhookMode.Global;

    /// <summary>Used when <see cref="Mode"/> is <see cref="WebhookMode.Global"/>.</summary>
    public WebhookEndpointConfig? Global { get; set; }

    /// <summary>
    /// Used when <see cref="Mode"/> is <see cref="WebhookMode.PerTenant"/>.
    /// Keys are tenant IDs (case-insensitive). An optional <c>"default"</c> key acts as
    /// a fallback for messages whose TenantId is null or not found.
    /// </summary>
    public Dictionary<string, WebhookEndpointConfig> Tenants { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);
}

public enum WebhookMode
{
    /// <summary>One global endpoint receives every outbox message.</summary>
    Global,

    /// <summary>Each tenant has its own endpoint; messages are routed by TenantId.</summary>
    PerTenant
}

/// <summary>Connection details for a single webhook endpoint.</summary>
public class WebhookEndpointConfig
{
    public string Url { get; set; } = default!;

    /// <summary>HMAC-SHA256 signing secret shared with the receiver.</summary>
    public string Secret { get; set; } = default!;

    /// <summary>Maximum delivery attempts before a message is dead-lettered. Defaults to 5.</summary>
    public int MaxRetries { get; set; } = 5;

    /// <summary>Per-request HTTP timeout in seconds. Defaults to 30.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Extra HTTP headers appended to every delivery request for this endpoint.</summary>
    public Dictionary<string, string>? CustomHeaders { get; set; }
}
