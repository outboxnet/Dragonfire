using InboxNet.Models;

namespace InboxNet.Interfaces;

/// <summary>
/// Provider-specific webhook adapter. Validates and parses a <see cref="WebhookRequestContext"/>
/// in one call. For implementations that benefit from separating validation from parsing —
/// independent testing, shared validators, reuse across payload formats — implement
/// <see cref="IWebhookSignatureValidator"/> and <see cref="IWebhookPayloadMapper"/> separately
/// and register them via <c>AddProvider&lt;TValidator, TMapper&gt;(providerKey)</c>; the
/// registration composes the pair into an <see cref="IWebhookProvider"/> automatically.
/// </summary>
public interface IWebhookProvider
{
    /// <summary>
    /// Stable routing key for this provider (e.g. <c>"stripe"</c>, <c>"github"</c>).
    /// Persisted on every received message and used to scope dedup and handler matching.
    /// </summary>
    string Key { get; }

    /// <summary>
    /// Validates and parses the context. A signature mismatch or malformed body should
    /// return <see cref="WebhookParseResult.Invalid"/>.
    /// </summary>
    Task<WebhookParseResult> ParseAsync(
        WebhookRequestContext context,
        CancellationToken ct = default);
}
