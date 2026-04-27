namespace InboxNet.Models;

/// <summary>
/// Output of <see cref="Interfaces.IWebhookProvider.ParseAsync"/>. Either the request was
/// parsed and validated successfully (<see cref="IsValid"/> is <c>true</c>) and the fields
/// describe a canonical inbox message, or it was rejected with <see cref="InvalidReason"/>
/// set. Providers return invalid results for: signature mismatch, malformed body,
/// unknown event type, or any other reason a receiver should respond with HTTP 400.
/// </summary>
public sealed record WebhookParseResult
{
    public bool IsValid { get; init; }
    public string? InvalidReason { get; init; }

    public string EventType { get; init; } = default!;
    public string Payload { get; init; } = default!;
    public string ContentSha256 { get; init; } = default!;

    /// <summary>Provider-supplied stable event ID, when available (preferred for dedup).</summary>
    public string? ProviderEventId { get; init; }

    /// <summary>Partition key for ordered per-entity processing. Null = unordered.</summary>
    public string? EntityId { get; init; }
    public string? TenantId { get; init; }

    public Dictionary<string, string>? Headers { get; init; }
    public string? CorrelationId { get; init; }

    public static WebhookParseResult Invalid(string reason) =>
        new() { IsValid = false, InvalidReason = reason };

    public static WebhookParseResult Valid(
        string eventType,
        string payload,
        string contentSha256,
        string? providerEventId = null,
        string? entityId = null,
        string? tenantId = null,
        Dictionary<string, string>? headers = null,
        string? correlationId = null) =>
        new()
        {
            IsValid = true,
            EventType = eventType,
            Payload = payload,
            ContentSha256 = contentSha256,
            ProviderEventId = providerEventId,
            EntityId = entityId,
            TenantId = tenantId,
            Headers = headers,
            CorrelationId = correlationId
        };
}
