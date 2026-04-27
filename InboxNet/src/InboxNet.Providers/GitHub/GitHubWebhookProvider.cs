using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using InboxNet.Interfaces;
using InboxNet.Models;
using InboxNet.Options;

namespace InboxNet.Providers.GitHub;

/// <summary>
/// Validates GitHub webhook signatures per
/// https://docs.github.com/en/webhooks/using-webhooks/validating-webhook-deliveries.
/// Headers used:
/// <list type="bullet">
/// <item><description><c>X-Hub-Signature-256</c> — HMAC-SHA256 of the body, prefixed with <c>sha256=</c>.</description></item>
/// <item><description><c>X-GitHub-Event</c> — event type (e.g. <c>push</c>, <c>pull_request</c>).</description></item>
/// <item><description><c>X-GitHub-Delivery</c> — stable per-delivery UUID, used for dedup.</description></item>
/// </list>
/// </summary>
public sealed class GitHubWebhookProvider : IWebhookProvider
{
    private const string SignaturePrefix = "sha256=";
    private const string SignatureHeader = "X-Hub-Signature-256";
    private const string EventHeader = "X-GitHub-Event";
    private const string DeliveryHeader = "X-GitHub-Delivery";

    private readonly GitHubWebhookOptions _options;
    private readonly byte[] _signingKey;
    private readonly bool _alwaysComputeSha;

    public GitHubWebhookProvider(
        IOptions<GitHubWebhookOptions> options,
        IOptions<InboxOptions> inboxOptions)
    {
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.Secret))
            throw new InvalidOperationException("GitHubWebhookOptions.Secret must be set.");
        _signingKey = Encoding.UTF8.GetBytes(_options.Secret);
        _alwaysComputeSha = inboxOptions.Value.AlwaysComputeContentSha256;
    }

    public string Key => _options.Key;

    public Task<WebhookParseResult> ParseAsync(WebhookRequestContext context, CancellationToken ct = default)
    {
        var rawBody = context.RawBody;

        if (!context.Headers.TryGetValue(SignatureHeader, out var signature) || string.IsNullOrEmpty(signature))
            return Task.FromResult(WebhookParseResult.Invalid($"Missing {SignatureHeader} header"));

        if (!signature.StartsWith(SignaturePrefix, StringComparison.Ordinal))
            return Task.FromResult(WebhookParseResult.Invalid($"{SignatureHeader} must start with 'sha256='"));

        var signatureHex = signature[SignaturePrefix.Length..];
        var expected = WebhookSignatureHelpers.ComputeHmacSha256(_signingKey, rawBody);

        if (!WebhookSignatureHelpers.FixedTimeHexEquals(signatureHex, expected))
            return Task.FromResult(WebhookParseResult.Invalid("Signature mismatch"));

        if (!context.Headers.TryGetValue(EventHeader, out var eventType) || string.IsNullOrEmpty(eventType))
            return Task.FromResult(WebhookParseResult.Invalid($"Missing {EventHeader} header"));

        // X-GitHub-Delivery is a per-delivery UUID that's stable across retries, making it
        // ideal as the provider event ID for dedup.
        var deliveryId = context.Headers.TryGetValue(DeliveryHeader, out var did) ? did : null;

        // Try to extract repo full_name for the partition key so ordered processing groups
        // events by repo rather than emitting one giant serial stream per tenant.
        string? entityId = null;
        string? tenantId = null;
        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;

            if (root.TryGetProperty("repository", out var repoElem))
            {
                if (repoElem.TryGetProperty("full_name", out var repoFull) &&
                    repoFull.ValueKind == JsonValueKind.String)
                    entityId = repoFull.GetString();
            }
            if (root.TryGetProperty("installation", out var instElem) &&
                instElem.TryGetProperty("id", out var instIdElem) &&
                instIdElem.ValueKind == JsonValueKind.Number)
            {
                tenantId = instIdElem.GetInt64().ToString();
            }
        }
        catch (JsonException)
        {
            return Task.FromResult(WebhookParseResult.Invalid("Body is not valid JSON"));
        }

        var contentSha = WebhookSignatureHelpers.ComputeContentSha256IfNeeded(
            rawBody, deliveryId, _alwaysComputeSha);

        return Task.FromResult(WebhookParseResult.Valid(
            eventType: eventType.ToString(),
            payload: rawBody,
            contentSha256: contentSha,
            providerEventId: deliveryId,
            entityId: entityId,
            tenantId: tenantId,
            headers: null,
            correlationId: deliveryId));
    }
}
