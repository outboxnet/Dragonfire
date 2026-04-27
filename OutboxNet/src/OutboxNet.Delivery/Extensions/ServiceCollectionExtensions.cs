using Microsoft.Extensions.DependencyInjection;
using OutboxNet.Extensions;
using OutboxNet.Interfaces;
using OutboxNet.Options;

namespace OutboxNet.Delivery.Extensions;

public static class ServiceCollectionExtensions
{
    public static IOutboxNetBuilder AddWebhookDelivery(
        this IOutboxNetBuilder builder,
        Action<WebhookDeliveryOptions>? configure = null)
    {
        var options = new WebhookDeliveryOptions();
        configure?.Invoke(options);

        builder.Services.Configure<WebhookDeliveryOptions>(o =>
        {
            o.HttpTimeout = options.HttpTimeout;
            o.Retry.MaxRetries = options.Retry.MaxRetries;
            o.Retry.BaseDelay = options.Retry.BaseDelay;
            o.Retry.MaxDelay = options.Retry.MaxDelay;
            o.Retry.JitterFactor = options.Retry.JitterFactor;
        });

        builder.Services.Configure<RetryPolicyOptions>(o =>
        {
            o.MaxRetries = options.Retry.MaxRetries;
            o.BaseDelay = options.Retry.BaseDelay;
            o.MaxDelay = options.Retry.MaxDelay;
            o.JitterFactor = options.Retry.JitterFactor;
        });

        builder.Services.AddHttpClient<IWebhookDeliverer, HttpWebhookDeliverer>(client =>
        {
            // Disable the global HttpClient timeout. Each request is controlled by
            // a per-subscription CancellationToken (subscription.Timeout via CancelAfter)
            // so a too-small global timeout would fire the wrong exception type.
            client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
        });

        builder.Services.AddSingleton<IRetryPolicy, ExponentialBackoffRetryPolicy>();

        return builder;
    }
}
