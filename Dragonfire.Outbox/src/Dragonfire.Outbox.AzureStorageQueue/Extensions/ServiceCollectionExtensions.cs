using Microsoft.Extensions.DependencyInjection;
using Dragonfire.Outbox.AzureStorageQueue.Options;
using Dragonfire.Outbox.Extensions;
using Dragonfire.Outbox.Interfaces;

namespace Dragonfire.Outbox.AzureStorageQueue.Extensions;

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
