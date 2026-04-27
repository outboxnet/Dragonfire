using Microsoft.Extensions.DependencyInjection;
using OutboxNet.Extensions;
using OutboxNet.Interfaces;
using OutboxNet.Options;

namespace OutboxNet.Processor.Extensions;

public static class ServiceCollectionExtensions
{
    public static IOutboxNetBuilder AddBackgroundProcessor(
        this IOutboxNetBuilder builder,
        Action<ProcessorOptions>? configure = null)
    {
        var options = new ProcessorOptions();
        configure?.Invoke(options);

        builder.Services.Configure<ProcessorOptions>(o =>
        {
            o.ColdPollingInterval = options.ColdPollingInterval;
        });

        // Singleton: the pipeline only takes singletons (IServiceScopeFactory, IRetryPolicy,
        // IOptions, ILogger) and creates its own child scopes internally per batch/message.
        builder.Services.AddSingleton<IOutboxProcessor, OutboxProcessingPipeline>();
        builder.Services.AddHostedService<OutboxProcessorService>();

        return builder;
    }
}
