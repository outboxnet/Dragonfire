using System.Diagnostics;
using Microsoft.Extensions.Logging;
using InboxNet.Interfaces;
using InboxNet.Models;
using InboxNet.Observability;

namespace InboxNet.EntityFrameworkCore;

internal sealed class EfCoreInboxPublisher : IInboxPublisher
{
    private readonly IInboxStore _store;
    private readonly IInboxSignal _signal;
    private readonly ILogger<EfCoreInboxPublisher> _logger;

    public EfCoreInboxPublisher(
        IInboxStore store,
        IInboxSignal signal,
        ILogger<EfCoreInboxPublisher> logger)
    {
        _store = store;
        _signal = signal;
        _logger = logger;
    }

    public async Task<InboxPublishResult> PublishAsync(
        string providerKey,
        WebhookParseResult parse,
        CancellationToken cancellationToken = default)
    {
        if (!parse.IsValid)
            throw new InvalidOperationException(
                $"Cannot publish an invalid parse result. Reason: {parse.InvalidReason}");

        using var activity = InboxActivitySource.Source.StartActivity("inbox.publish");
        activity?.SetTag("inbox.provider_key", providerKey);
        activity?.SetTag("inbox.event_type", parse.EventType);

        var message = new InboxMessage
        {
            Id = Guid.NewGuid(),
            ProviderKey = providerKey,
            EventType = parse.EventType,
            Payload = parse.Payload,
            ContentSha256 = parse.ContentSha256,
            ProviderEventId = parse.ProviderEventId,
            DedupKey = parse.ProviderEventId ?? parse.ContentSha256,
            Status = InboxMessageStatus.Pending,
            RetryCount = 0,
            ReceivedAt = DateTimeOffset.UtcNow,
            Headers = parse.Headers,
            CorrelationId = parse.CorrelationId,
            TraceId = Activity.Current?.TraceId.ToString(),
            TenantId = parse.TenantId,
            EntityId = parse.EntityId
        };

        var (id, isDuplicate) = await _store.InsertOrGetDuplicateAsync(message, cancellationToken);

        if (isDuplicate)
        {
            InboxMetrics.MessagesDuplicate.Add(1,
                new KeyValuePair<string, object?>("provider", providerKey),
                new KeyValuePair<string, object?>("event_type", parse.EventType));

            _logger.LogDebug(
                "Duplicate inbox message for provider {ProviderKey}, dedupKey {DedupKey} — mapped to existing id {Id}",
                providerKey, message.DedupKey, id);

            activity?.SetTag("inbox.duplicate", true);
            activity?.SetTag("inbox.message_id", id.ToString());
            return new InboxPublishResult(id, IsDuplicate: true);
        }

        InboxMetrics.MessagesReceived.Add(1,
            new KeyValuePair<string, object?>("provider", providerKey),
            new KeyValuePair<string, object?>("event_type", parse.EventType));

        activity?.SetTag("inbox.message_id", id.ToString());

        _logger.LogDebug(
            "Accepted inbox message {Id} from provider {ProviderKey}, event {EventType}",
            id, providerKey, parse.EventType);

        // Fire-and-forget signal — the commit has already happened. Hot-path processor
        // will pick this up in under a millisecond on the same instance.
        _signal.Notify(id);

        return new InboxPublishResult(id, IsDuplicate: false);
    }
}
