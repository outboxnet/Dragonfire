using System.Diagnostics.Metrics;

namespace OutboxNet.Observability;

public static class OutboxMetrics
{
    public static readonly Meter Meter = new("OutboxNet", "1.0.0");

    public static readonly Counter<long> MessagesPublished =
        Meter.CreateCounter<long>("outbox.messages.published", description: "Total messages published to the outbox");

    public static readonly Counter<long> MessagesProcessed =
        Meter.CreateCounter<long>("outbox.messages.processed", description: "Total messages successfully processed");

    public static readonly Counter<long> MessagesFailed =
        Meter.CreateCounter<long>("outbox.messages.failed", description: "Total messages that failed processing");

    public static readonly Counter<long> MessagesDeadLettered =
        Meter.CreateCounter<long>("outbox.messages.dead_lettered", description: "Total messages sent to dead letter");

    public static readonly Counter<long> DeliveryAttempts =
        Meter.CreateCounter<long>("outbox.delivery.attempts", description: "Total webhook delivery attempts");

    public static readonly Counter<long> DeliverySuccesses =
        Meter.CreateCounter<long>("outbox.delivery.successes", description: "Total successful webhook deliveries");

    public static readonly Counter<long> DeliveryFailures =
        Meter.CreateCounter<long>("outbox.delivery.failures", description: "Total failed webhook deliveries");

    public static readonly Histogram<double> DeliveryDuration =
        Meter.CreateHistogram<double>("outbox.delivery.duration_ms", "ms", "Webhook delivery duration in milliseconds");

    public static readonly Histogram<double> ProcessingDuration =
        Meter.CreateHistogram<double>("outbox.processing.duration_ms", "ms", "Batch processing duration in milliseconds");

    public static readonly Counter<long> BatchesProcessed =
        Meter.CreateCounter<long>("outbox.batches.processed", description: "Total batches processed");

    public static readonly Histogram<int> BatchSize =
        Meter.CreateHistogram<int>("outbox.batch.size", "messages", "Number of messages per batch");
}
