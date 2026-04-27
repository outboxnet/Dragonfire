using System.Text.Json;
using Azure.Storage.Queues;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OutboxNet.AzureStorageQueue.Options;
using OutboxNet.Interfaces;
using OutboxNet.Models;

namespace OutboxNet.AzureStorageQueue;

internal sealed class AzureStorageQueuePublisher : IMessagePublisher
{
    private readonly QueueClient _queueClient;
    private readonly AzureStorageQueueOptions _options;
    private readonly ILogger<AzureStorageQueuePublisher> _logger;

    public AzureStorageQueuePublisher(
        IOptions<AzureStorageQueueOptions> options,
        ILogger<AzureStorageQueuePublisher> logger)
    {
        _options = options.Value;
        _logger = logger;
        _queueClient = new QueueClient(_options.ConnectionString, _options.QueueName,
            new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 });
    }

    public async Task PublishAsync(OutboxMessage message, CancellationToken ct = default)
    {
        await _queueClient.CreateIfNotExistsAsync(cancellationToken: ct);

        var envelope = new QueueMessageEnvelope
        {
            MessageId = message.Id,
            EventType = message.EventType,
            Payload = message.Payload,
            CorrelationId = message.CorrelationId,
            TraceId = message.TraceId,
            Headers = message.Headers,
            CreatedAt = message.CreatedAt
        };

        var json = JsonSerializer.Serialize(envelope);

        await _queueClient.SendMessageAsync(
            json,
            _options.VisibilityTimeout,
            _options.MessageTimeToLive,
            ct);

        _logger.LogDebug("Published message {MessageId} to Azure Storage Queue {QueueName}",
            message.Id, _options.QueueName);
    }
}

internal sealed class QueueMessageEnvelope
{
    public Guid MessageId { get; set; }
    public string EventType { get; set; } = default!;
    public string Payload { get; set; } = default!;
    public string? CorrelationId { get; set; }
    public string? TraceId { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
