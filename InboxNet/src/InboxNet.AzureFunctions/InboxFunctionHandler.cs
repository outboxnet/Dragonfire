using Microsoft.Extensions.Logging;
using InboxNet.Interfaces;
using InboxNet.Models;

namespace InboxNet.AzureFunctions;

/// <summary>
/// Scoped service: resolved once per function invocation. Injects scoped inbox services
/// (publisher, provider registry) directly, and the singleton processor for dispatch.
/// </summary>
internal sealed class InboxFunctionHandler : IInboxFunctionHandler
{
    private readonly IWebhookProviderRegistry _registry;
    private readonly IInboxPublisher _publisher;
    private readonly IInboxProcessor _processor;
    private readonly ILogger<InboxFunctionHandler> _logger;

    public InboxFunctionHandler(
        IWebhookProviderRegistry registry,
        IInboxPublisher publisher,
        IInboxProcessor processor,
        ILogger<InboxFunctionHandler> logger)
    {
        _registry = registry;
        _publisher = publisher;
        _processor = processor;
        _logger = logger;
    }

    public async Task<InboxReceiveResult> ReceiveAsync(
        string providerKey,
        string rawBody,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken ct)
    {
        var provider = _registry.Get(providerKey);
        if (provider is null)
        {
            _logger.LogWarning("No webhook provider registered for key {ProviderKey}", providerKey);
            return new InboxReceiveResult(InboxReceiveStatus.ProviderNotFound);
        }

        var context = new WebhookRequestContext
        {
            ProviderKey = providerKey,
            RawBody = rawBody,
            Headers = headers
        };

        var parse = await provider.ParseAsync(context, ct);

        if (!parse.IsValid)
        {
            _logger.LogWarning(
                "Webhook rejected by provider {ProviderKey}: {Reason}",
                providerKey, parse.InvalidReason);
            return new InboxReceiveResult(InboxReceiveStatus.Invalid, InvalidReason: parse.InvalidReason);
        }

        var result = await _publisher.PublishAsync(providerKey, parse, ct);

        _logger.LogDebug(
            "Webhook {ProviderKey}/{EventType} {Result} — messageId {MessageId}",
            providerKey, parse.EventType,
            result.IsDuplicate ? "duplicate" : "accepted",
            result.MessageId);

        return new InboxReceiveResult(
            result.IsDuplicate ? InboxReceiveStatus.Duplicate : InboxReceiveStatus.Accepted,
            MessageId: result.MessageId);
    }

    public Task DispatchAsync(CancellationToken ct) =>
        _processor.ProcessBatchAsync(ct);
}
