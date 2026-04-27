namespace OutboxNet.Options;

public class OutboxOptions
{
    public string SchemaName { get; set; } = "outbox";
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// How long a locked message is invisible to other processor instances.
    /// Must be longer than worst-case batch processing time:
    ///   ceil(BatchSize / MaxConcurrentDeliveries) × MaxConcurrentSubscriptionDeliveries × subscription.Timeout
    /// Default: 5 minutes.
    /// </summary>
    public TimeSpan DefaultVisibilityTimeout { get; set; } = TimeSpan.FromMinutes(5);

    public string InstanceId { get; set; } = $"{Environment.MachineName}-{Guid.NewGuid():N}";

    /// <summary>
    /// How many outbox messages are processed concurrently within a single batch.
    /// Each concurrent message opens its own DB scope and a pool of HTTP connections.
    /// Default: 10.
    /// </summary>
    public int MaxConcurrentDeliveries { get; set; } = 10;

    /// <summary>
    /// How many webhook subscriptions for a single message are delivered concurrently.
    /// With <c>MaxConcurrentDeliveries = 10</c> and <c>MaxConcurrentSubscriptionDeliveries = 4</c>
    /// there are at most 40 simultaneous outbound HTTP requests.
    /// Default: 4. Set to 1 to restore sequential subscription delivery.
    /// </summary>
    public int MaxConcurrentSubscriptionDeliveries { get; set; } = 4;

    public ProcessingMode ProcessingMode { get; set; } = ProcessingMode.DirectDelivery;

    /// <summary>
    /// When true (default), messages that share the same (TenantId, UserId, EntityId) partition key
    /// are processed strictly in order — a message for a given partition is not picked up until
    /// any previously locked message in that partition has finished. Messages with no partition
    /// keys set are unaffected and retain concurrent processing behaviour.
    /// Set to false to disable the ordering guarantee globally.
    /// </summary>
    public bool EnableOrderedProcessing { get; set; } = true;

    /// <summary>
    /// When set, only messages whose <c>TenantId</c> matches this value are picked up by
    /// <c>LockNextBatchAsync</c>. Use this to shard processing across multiple processor
    /// instances — each instance handles a dedicated tenant (or set of tenants).
    /// <c>null</c> (default) means all tenants are processed by this instance.
    /// </summary>
    public string? TenantFilter { get; set; }
}

public enum ProcessingMode
{
    DirectDelivery,
    QueueMediated
}
