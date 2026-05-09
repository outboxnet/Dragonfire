namespace Dragonfire.Outbox.AzureStorageQueueSample.Configuration;

/// <summary>
/// Bound from <c>AzureStorageQueue</c> in appsettings — same connection string + queue name used
/// by the outbox publisher, since consumer and publisher must agree on both.
/// </summary>
public sealed class SampleQueueConsumerOptions
{
    public string ConnectionString { get; set; } = "UseDevelopmentStorage=true";
    public string QueueName { get; set; } = "outbox-messages";
}
