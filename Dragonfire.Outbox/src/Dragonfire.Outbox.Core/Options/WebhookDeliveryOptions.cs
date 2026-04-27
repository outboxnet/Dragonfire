namespace Dragonfire.Outbox.Options;

public class WebhookDeliveryOptions
{
    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public RetryPolicyOptions Retry { get; set; } = new();
}
