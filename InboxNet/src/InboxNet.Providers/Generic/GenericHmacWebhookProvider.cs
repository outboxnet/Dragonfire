using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using InboxNet.Interfaces;
using InboxNet.Models;
using InboxNet.Options;

namespace InboxNet.Providers.Generic;

/// <summary>
/// Configurable HMAC-SHA256 webhook provider for services that don't have a built-in adapter.
/// Signature is computed over the raw body, hex-encoded, and sent as a single header
/// (optionally with a prefix like <c>sha256=</c>). Event type, ID, and partition key are
/// pulled from configurable headers or JSON paths — so the same provider class can be
/// instantiated multiple times under different keys for different upstream services.
/// </summary>
public sealed class GenericHmacWebhookProvider : IWebhookProvider
{
    private readonly GenericHmacWebhookOptions _options;
    private readonly byte[] _signingKey;
    private readonly bool _alwaysComputeSha;

    public GenericHmacWebhookProvider(
        GenericHmacWebhookOptions options,
        IOptions<InboxOptions> inboxOptions)
    {
        _options = options;
        if (string.IsNullOrWhiteSpace(_options.Secret))
            throw new InvalidOperationException(
                $"GenericHmacWebhookOptions.Secret must be set for provider '{_options.Key}'.");
        _signingKey = Encoding.UTF8.GetBytes(_options.Secret);
        _alwaysComputeSha = inboxOptions.Value.AlwaysComputeContentSha256;
    }

    public string Key => _options.Key;

    public Task<WebhookParseResult> ParseAsync(WebhookRequestContext context, CancellationToken ct = default)
    {
        var rawBody = context.RawBody;

        if (!context.Headers.TryGetValue(_options.SignatureHeader, out var signature) || string.IsNullOrEmpty(signature))
            return Task.FromResult(WebhookParseResult.Invalid($"Missing {_options.SignatureHeader} header"));

        if (!string.IsNullOrEmpty(_options.SignaturePrefix))
        {
            if (!signature.StartsWith(_options.SignaturePrefix, StringComparison.Ordinal))
                return Task.FromResult(WebhookParseResult.Invalid(
                    $"{_options.SignatureHeader} must start with '{_options.SignaturePrefix}'"));
            signature = signature[_options.SignaturePrefix.Length..];
        }

        var expected = WebhookSignatureHelpers.ComputeHmacSha256(_signingKey, rawBody);
        if (!WebhookSignatureHelpers.FixedTimeHexEquals(signature, expected))
            return Task.FromResult(WebhookParseResult.Invalid("Signature mismatch"));

        string? eventType = null;
        string? entityId = null;

        if (_options.EventTypeHeader is not null &&
            context.Headers.TryGetValue(_options.EventTypeHeader, out var evtHeader) &&
            !string.IsNullOrEmpty(evtHeader))
        {
            eventType = evtHeader;
        }

        if (eventType is null || _options.EntityIdJsonProperty is not null)
        {
            try
            {
                using var doc = JsonDocument.Parse(rawBody);
                var root = doc.RootElement;

                if (eventType is null)
                {
                    if (!root.TryGetProperty(_options.EventTypeJsonProperty, out var evtElem) ||
                        evtElem.ValueKind != JsonValueKind.String)
                        return Task.FromResult(WebhookParseResult.Invalid(
                            $"Missing '{_options.EventTypeJsonProperty}' field in body"));
                    eventType = evtElem.GetString();
                }

                if (_options.EntityIdJsonProperty is not null &&
                    root.TryGetProperty(_options.EntityIdJsonProperty, out var entElem))
                {
                    entityId = entElem.ValueKind switch
                    {
                        JsonValueKind.String => entElem.GetString(),
                        JsonValueKind.Number => entElem.ToString(),
                        _ => null
                    };
                }
            }
            catch (JsonException)
            {
                return Task.FromResult(WebhookParseResult.Invalid("Body is not valid JSON"));
            }
        }

        var providerEventId = _options.EventIdHeader is not null &&
                              context.Headers.TryGetValue(_options.EventIdHeader, out var eid) &&
                              !string.IsNullOrEmpty(eid)
            ? eid
            : null;

        var tenantId = _options.TenantIdHeader is not null &&
                       context.Headers.TryGetValue(_options.TenantIdHeader, out var tid) &&
                       !string.IsNullOrEmpty(tid)
            ? tid
            : null;

        var contentSha = WebhookSignatureHelpers.ComputeContentSha256IfNeeded(
            rawBody, providerEventId, _alwaysComputeSha);

        return Task.FromResult(WebhookParseResult.Valid(
            eventType: eventType!,
            payload: rawBody,
            contentSha256: contentSha,
            providerEventId: providerEventId,
            entityId: entityId,
            tenantId: tenantId,
            headers: null,
            correlationId: providerEventId));
    }
}
