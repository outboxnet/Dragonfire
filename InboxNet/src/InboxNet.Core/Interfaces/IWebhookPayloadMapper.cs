using InboxNet.Models;

namespace InboxNet.Interfaces;

/// <summary>
/// Parses an already-validated webhook body into the canonical <see cref="WebhookParseResult"/>.
/// Runs after <see cref="IWebhookSignatureValidator"/> in the pipeline composed by
/// <see cref="IWebhookProvider"/>, so implementations can assume the signature check
/// has passed.
/// </summary>
public interface IWebhookPayloadMapper
{
    Task<WebhookParseResult> MapAsync(
        WebhookRequestContext context,
        CancellationToken ct = default);
}
