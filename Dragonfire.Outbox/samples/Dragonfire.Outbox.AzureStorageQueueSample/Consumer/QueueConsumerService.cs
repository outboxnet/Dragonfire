using System.Text.Json;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Dragonfire.Outbox.AzureStorageQueueSample.Configuration;
using Microsoft.Extensions.Options;

namespace Dragonfire.Outbox.AzureStorageQueueSample.Consumer;

/// <summary>
/// Reads messages off the same Azure Storage Queue the outbox processor publishes to.
/// In a real system this would live in a separate worker service — keeping it in-process here
/// just makes the sample one <c>dotnet run</c>. The shape of the JSON envelope mirrors
/// <c>QueueMessageEnvelope</c> in <c>Dragonfire.Outbox.AzureStorageQueue</c>; we redeclare it
/// here because that type is internal to the connector (which is correct — consumers shouldn't
/// reach across the package boundary).
/// </summary>
public sealed class QueueConsumerService : BackgroundService
{
    private readonly QueueClient _queueClient;
    private readonly ILogger<QueueConsumerService> _logger;

    public QueueConsumerService(
        IOptions<SampleQueueConsumerOptions> options,
        ILogger<QueueConsumerService> logger)
    {
        var o = options.Value;
        _queueClient = new QueueClient(o.ConnectionString, o.QueueName,
            new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 });
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _queueClient.CreateIfNotExistsAsync(cancellationToken: stoppingToken);
        _logger.LogInformation("Queue consumer started — polling {Queue}", _queueClient.Name);

        while (!stoppingToken.IsCancellationRequested)
        {
            QueueMessage[] messages;
            try
            {
                // ReceiveMessages is a long-poll-friendly API — visibilityTimeout hides the message
                // from other consumers while we process it. If we crash before DeleteMessage, the
                // message reappears after that timeout and another consumer (or this one) retries.
                var response = await _queueClient.ReceiveMessagesAsync(
                    maxMessages: 16,
                    visibilityTimeout: TimeSpan.FromSeconds(60),
                    cancellationToken: stoppingToken);
                messages = response.Value;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error receiving messages from queue — backing off");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                continue;
            }

            if (messages.Length == 0)
            {
                // Azure Storage Queues have no native long polling — sleep briefly to avoid a tight loop.
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                continue;
            }

            foreach (var msg in messages)
            {
                try
                {
                    var envelope = JsonSerializer.Deserialize<QueueEnvelope>(msg.Body.ToString());
                    if (envelope is null)
                    {
                        _logger.LogWarning("Discarding empty/unparseable queue message {MessageId}", msg.MessageId);
                    }
                    else
                    {
                        _logger.LogInformation(
                            "Consumed {EventType} (msg {OutboxMessageId}) at {CreatedAt:O}: {Payload}",
                            envelope.EventType, envelope.MessageId, envelope.CreatedAt, envelope.Payload);
                    }

                    // Delete = ack. Only deletes if our pop receipt is still valid (i.e. the visibility
                    // timeout hasn't expired and another consumer hasn't taken over).
                    await _queueClient.DeleteMessageAsync(msg.MessageId, msg.PopReceipt, stoppingToken);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    // Don't delete on failure — let the visibility timeout expire and re-deliver.
                    // After Azure's default max DequeueCount (~5), the message will sit dead in the queue;
                    // a real consumer would move it to a poison queue here.
                    _logger.LogError(ex, "Error handling queue message {MessageId} — will re-deliver after timeout",
                        msg.MessageId);
                }
            }
        }

        _logger.LogInformation("Queue consumer stopped");
    }

    /// <summary>Shape mirrors <c>Dragonfire.Outbox.AzureStorageQueue.QueueMessageEnvelope</c>.</summary>
    private sealed class QueueEnvelope
    {
        public Guid MessageId { get; set; }
        public string EventType { get; set; } = default!;
        public string Payload { get; set; } = default!;
        public string? CorrelationId { get; set; }
        public string? TraceId { get; set; }
        public Dictionary<string, string>? Headers { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }
}
