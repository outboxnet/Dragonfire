using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using InboxNet.Interfaces;
using InboxNet.Models;
using InboxNet.Options;

namespace InboxNet.Providers.Stripe;

/// <summary>
/// Validates Stripe webhook signatures and extracts the event's canonical fields.
/// Implements the v1 signature scheme documented at
/// https://stripe.com/docs/webhooks/signatures. The signature header format is:
/// <c>Stripe-Signature: t=TIMESTAMP,v1=HEX_HMAC[,v1=HEX_HMAC]</c> — multiple
/// <c>v1</c> values occur during secret rotation; any match is accepted.
/// </summary>
public sealed class StripeWebhookProvider : IWebhookProvider
{
    private const string SignatureHeader = "Stripe-Signature";
    private readonly StripeWebhookOptions _options;
    private readonly byte[] _signingKey;
    private readonly bool _alwaysComputeSha;

    public StripeWebhookProvider(
        IOptions<StripeWebhookOptions> options,
        IOptions<InboxOptions> inboxOptions)
    {
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.SigningSecret))
            throw new InvalidOperationException(
                "StripeWebhookOptions.SigningSecret must be set.");
        _signingKey = Encoding.UTF8.GetBytes(_options.SigningSecret);
        _alwaysComputeSha = inboxOptions.Value.AlwaysComputeContentSha256;
    }

    public string Key => _options.Key;

    public Task<WebhookParseResult> ParseAsync(WebhookRequestContext context, CancellationToken ct = default)
    {
        var rawBody = context.RawBody;

        if (!context.Headers.TryGetValue(SignatureHeader, out var sigHeader) || string.IsNullOrEmpty(sigHeader))
            return Task.FromResult(WebhookParseResult.Invalid($"Missing {SignatureHeader} header"));

        if (!TryParseSignatureHeader(sigHeader, out var timestamp, out var v1Signatures))
            return Task.FromResult(WebhookParseResult.Invalid($"Malformed {SignatureHeader} header"));

        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(nowUnix - timestamp) > _options.ToleranceSeconds)
            return Task.FromResult(WebhookParseResult.Invalid("Webhook timestamp outside tolerance"));

        var signedPayload = $"{timestamp}.{rawBody}";
        var expected = WebhookSignatureHelpers.ComputeHmacSha256(_signingKey, signedPayload);

        var matched = false;
        foreach (var candidate in v1Signatures)
        {
            if (WebhookSignatureHelpers.FixedTimeHexEquals(candidate, expected))
            {
                matched = true;
                break;
            }
        }

        if (!matched)
            return Task.FromResult(WebhookParseResult.Invalid("Signature mismatch"));

        string eventType;
        string? eventId;
        string? accountId;
        string? entityId;
        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeElem) || typeElem.ValueKind != JsonValueKind.String)
                return Task.FromResult(WebhookParseResult.Invalid("Missing 'type' field"));
            eventType = typeElem.GetString()!;

            eventId = root.TryGetProperty("id", out var idElem) && idElem.ValueKind == JsonValueKind.String
                ? idElem.GetString()
                : null;

            // Stripe Connect events include "account" at the top level.
            accountId = root.TryGetProperty("account", out var acctElem) && acctElem.ValueKind == JsonValueKind.String
                ? acctElem.GetString()
                : null;

            // Extract the primary object's ID for ordered per-entity processing.
            entityId = null;
            if (root.TryGetProperty("data", out var dataElem) &&
                dataElem.TryGetProperty("object", out var objElem) &&
                objElem.TryGetProperty("id", out var objIdElem) &&
                objIdElem.ValueKind == JsonValueKind.String)
            {
                entityId = objIdElem.GetString();
            }
        }
        catch (JsonException)
        {
            return Task.FromResult(WebhookParseResult.Invalid("Body is not valid JSON"));
        }

        var contentSha = WebhookSignatureHelpers.ComputeContentSha256IfNeeded(
            rawBody, eventId, _alwaysComputeSha);

        return Task.FromResult(WebhookParseResult.Valid(
            eventType: eventType,
            payload: rawBody,
            contentSha256: contentSha,
            providerEventId: eventId,
            entityId: entityId,
            tenantId: accountId,
            headers: null,
            correlationId: context.Headers.TryGetValue("Idempotency-Key", out var idk) ? idk : null));
    }

    private static bool TryParseSignatureHeader(string header, out long timestamp, out List<string> v1Signatures)
    {
        timestamp = 0;
        v1Signatures = new List<string>();

        foreach (var part in header.Split(','))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            var name = part[..eq].Trim();
            var value = part[(eq + 1)..].Trim();

            if (name == "t")
            {
                if (!long.TryParse(value, out timestamp))
                    return false;
            }
            else if (name == "v1")
            {
                v1Signatures.Add(value);
            }
        }

        return timestamp > 0 && v1Signatures.Count > 0;
    }
}
