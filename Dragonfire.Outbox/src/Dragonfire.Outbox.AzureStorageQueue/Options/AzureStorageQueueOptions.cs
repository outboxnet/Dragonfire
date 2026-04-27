namespace Dragonfire.Outbox.AzureStorageQueue.Options;

public class AzureStorageQueueOptions
{
    public string ConnectionString { get; set; } = default!;
    public string QueueName { get; set; } = "outbox-messages";
    public TimeSpan? VisibilityTimeout { get; set; }
    public TimeSpan? MessageTimeToLive { get; set; }
}
