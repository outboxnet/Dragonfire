namespace InboxNet.Models;

/// <summary>
/// Host-agnostic representation of an incoming webhook request. Produced by the hosting
/// layer (ASP.NET minimal API, Azure Functions isolated worker, etc.) and passed to
/// <see cref="Interfaces.IWebhookProvider"/>,
/// <see cref="Interfaces.IWebhookSignatureValidator"/>, and
/// <see cref="Interfaces.IWebhookPayloadMapper"/>. Keeps webhook processing decoupled
/// from any particular HTTP host abstraction.
/// </summary>
public sealed record WebhookRequestContext
{
    /// <summary>Provider routing key — typically the matched route segment.</summary>
    public required string ProviderKey { get; init; }

    /// <summary>Verbatim request body as a UTF-8 string. Used for signature verification and SHA-256.</summary>
    public required string RawBody { get; init; }

    /// <summary>
    /// Request headers. Lookups are case-insensitive — build the dictionary with
    /// <see cref="StringComparer.OrdinalIgnoreCase"/>. Multi-valued headers should be
    /// joined with a comma per RFC 7230 §3.2.2.
    /// </summary>
    public required IReadOnlyDictionary<string, string> Headers { get; init; }

    /// <summary>Optional request path (e.g. <c>/api/webhooks/stripe</c>).</summary>
    public string? RoutePath { get; init; }

    /// <summary>Optional query string, leading <c>?</c> included when present.</summary>
    public string? QueryString { get; init; }
}
