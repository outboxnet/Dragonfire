using System.Diagnostics.Metrics;

namespace InboxNet.Observability;

public static class InboxMetrics
{
    public static readonly Meter Meter = new("InboxNet.Inbox", "1.0.0");

    public static readonly Counter<long> MessagesReceived =
        Meter.CreateCounter<long>("inbox.messages.received", description: "Total inbox messages accepted (excluding duplicates)");

    public static readonly Counter<long> MessagesDuplicate =
        Meter.CreateCounter<long>("inbox.messages.duplicate", description: "Total inbox messages rejected by dedup");

    public static readonly Counter<long> MessagesInvalid =
        Meter.CreateCounter<long>("inbox.messages.invalid", description: "Total inbox requests rejected by provider validation");

    public static readonly Counter<long> MessagesProcessed =
        Meter.CreateCounter<long>("inbox.messages.processed", description: "Total inbox messages processed successfully");

    public static readonly Counter<long> MessagesFailed =
        Meter.CreateCounter<long>("inbox.messages.failed", description: "Total inbox messages that failed handler dispatch");

    public static readonly Counter<long> MessagesDeadLettered =
        Meter.CreateCounter<long>("inbox.messages.dead_lettered", description: "Total inbox messages moved to dead letter");

    public static readonly Counter<long> HandlerAttempts =
        Meter.CreateCounter<long>("inbox.handler.attempts", description: "Total handler invocations");

    public static readonly Counter<long> HandlerSuccesses =
        Meter.CreateCounter<long>("inbox.handler.successes", description: "Total successful handler invocations");

    public static readonly Counter<long> HandlerFailures =
        Meter.CreateCounter<long>("inbox.handler.failures", description: "Total failed handler invocations");

    public static readonly Histogram<double> HandlerDuration =
        Meter.CreateHistogram<double>("inbox.handler.duration_ms", "ms", "Handler execution duration in milliseconds");

    public static readonly Histogram<double> ProcessingDuration =
        Meter.CreateHistogram<double>("inbox.processing.duration_ms", "ms", "Batch processing duration in milliseconds");

    public static readonly Counter<long> BatchesProcessed =
        Meter.CreateCounter<long>("inbox.batches.processed", description: "Total batches dispatched");

    public static readonly Histogram<int> BatchSize =
        Meter.CreateHistogram<int>("inbox.batch.size", "messages", "Number of messages per batch");
}
