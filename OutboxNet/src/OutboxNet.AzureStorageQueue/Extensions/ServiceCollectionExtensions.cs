using Microsoft.Extensions.DependencyInjection;
using OutboxNet.AzureStorageQueue.Options;
using OutboxNet.Extensions;
using OutboxNet.Interfaces;

namespace OutboxNet.AzureStorageQueue.Extensions;

public static class ServiceCollectionExtensions
{
    public static IOutboxNetBuilder UseAzureStorageQueue(
        this IOutboxNetBuilder builder,
        Action<AzureStorageQueueOptions> configure)
    {
        var options = new AzureStorageQueueOptions();
        configure(options);

        builder.Services.Configure<AzureStorageQueueOptions>(o =>
        {
            o.ConnectionString = options.ConnectionString;
            o.QueueName = options.QueueName;
            o.VisibilityTimeout = options.VisibilityTimeout;
            o.MessageTimeToLive = options.MessageTimeToLive;
        });

        builder.Services.AddSingleton<IMessagePublisher, AzureStorageQueuePublisher>();

        return builder;
    }
}
