using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dragonfire.Inbox.Interfaces;
using Dragonfire.Inbox.Models;

namespace Dragonfire.WebhookPlatform.SampleApp.Inbox.LoopbackProvider;

/// <summary>
/// Maps an outbox-shaped POST into the canonical inbox message. The outbox sends the user's
/// payload as the JSON body and exposes routing metadata via <c>X-Outbox-*</c> headers, so we
/// pull <see cref="WebhookParseResult.EventType"/> from <c>X-Outbox-Event</c> and use
/// <c>X-Outbox-Message-Id</c> as the dedup key (so retries of the same outbox row collapse
/// into one inbox row).
/// </summary>
public sealed class LoopbackPayloadMapper : IWebhookPayloadMapper
{
    public Task<WebhookParseResult> MapAsync(
        WebhookRequestContext context, CancellationToken ct = default)
    {
        if (!context.Headers.TryGetValue("X-Outbox-Event", out var eventType) ||
            string.IsNullOrWhiteSpace(eventType))
            return Task.FromResult(WebhookParseResult.Invalid("Missing X-Outbox-Event header"));

        context.Headers.TryGetValue("X-Outbox-Message-Id", out var providerEventId);
        context.Headers.TryGetValue("X-Outbox-Correlation-Id", out var correlationId);

        // Pull tenant + entity directly from the payload — the outbox preserves whatever
        // shape the publisher passed, so we know the JSON looks like { tenantId, orderId, ... }.
        string? tenantId = null;
        string? entityId = null;
        try
        {
            using var doc = JsonDocument.Parse(context.RawBody);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (TryReadString(root, "tenantId", out var t)) tenantId = t;
                if (TryReadString(root, "orderId", out var o)) entityId = o;
            }
        }
        catch (JsonException)
        {
            return Task.FromResult(WebhookParseResult.Invalid("Body is not valid JSON"));
        }

        return Task.FromResult(WebhookParseResult.Valid(
            eventType: eventType,
            payload: context.RawBody,
            contentSha256: ComputeSha256Hex(context.RawBody),
            providerEventId: providerEventId,
            entityId: entityId,
            tenantId: tenantId,
            headers: null,
            correlationId: correlationId));
    }

    private static bool TryReadString(JsonElement root, string property, out string? value)
    {
        if (root.TryGetProperty(property, out var elem) && elem.ValueKind == JsonValueKind.String)
        {
            value = elem.GetString();
            return value is not null;
        }
        value = null;
        return false;
    }

    private static string ComputeSha256Hex(string body)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(body), hash);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
