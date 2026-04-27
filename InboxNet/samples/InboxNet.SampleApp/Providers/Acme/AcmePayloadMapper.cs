using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InboxNet.Interfaces;
using InboxNet.Models;

namespace InboxNet.SampleApp.Providers.Acme;

/// <summary>
/// Demo payload mapper for "Acme". Pulls <c>event_type</c>, <c>event_id</c>, and
/// <c>account_id</c> from the JSON body to populate the canonical inbox fields.
/// Separated from <see cref="AcmeSignatureValidator"/> so payload-parsing tests don't
/// need to fabricate valid signatures.
/// </summary>
public sealed class AcmePayloadMapper : IWebhookPayloadMapper
{
    public Task<WebhookParseResult> MapAsync(
        WebhookRequestContext context, CancellationToken ct = default)
    {
        try
        {
            using var doc = JsonDocument.Parse(context.RawBody);
            var root = doc.RootElement;

            if (!root.TryGetProperty("event_type", out var typeElem) ||
                typeElem.ValueKind != JsonValueKind.String)
                return Task.FromResult(WebhookParseResult.Invalid("Missing 'event_type'"));

            var eventType = typeElem.GetString()!;

            var eventId = root.TryGetProperty("event_id", out var idElem) &&
                          idElem.ValueKind == JsonValueKind.String
                ? idElem.GetString()
                : null;

            var accountId = root.TryGetProperty("account_id", out var acctElem) &&
                            acctElem.ValueKind == JsonValueKind.String
                ? acctElem.GetString()
                : null;

            var entityId = root.TryGetProperty("resource_id", out var resElem) &&
                           resElem.ValueKind == JsonValueKind.String
                ? resElem.GetString()
                : null;

            var contentSha = ComputeSha256Hex(context.RawBody);

            return Task.FromResult(WebhookParseResult.Valid(
                eventType: eventType,
                payload: context.RawBody,
                contentSha256: contentSha,
                providerEventId: eventId,
                entityId: entityId,
                tenantId: accountId,
                headers: null,
                correlationId: eventId));
        }
        catch (JsonException)
        {
            return Task.FromResult(WebhookParseResult.Invalid("Body is not valid JSON"));
        }
    }

    private static string ComputeSha256Hex(string body)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(body), hash);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
