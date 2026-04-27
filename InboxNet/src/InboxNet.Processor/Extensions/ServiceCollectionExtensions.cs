using Microsoft.Extensions.DependencyInjection;
using InboxNet.Extensions;
using InboxNet.Interfaces;
using InboxNet.Options;
using InboxNet.Processor.Purge;

namespace InboxNet.Processor.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the inbox background dispatcher. Runs a hot-path channel drain for
    /// sub-millisecond same-instance dispatch and a periodic cold-path batch scan for
    /// cross-instance messages, retries, and channel-overflow recovery.
    /// </summary>
    public static IInboxNetBuilder AddBackgroundDispatcher(
        this IInboxNetBuilder builder,
        Action<InboxProcessorOptions>? configure = null)
    {
        var optionsBuilder = builder.Services.AddOptions<InboxProcessorOptions>();
        if (configure is not null) optionsBuilder.Configure(configure);

        // Singleton: pipeline only consumes singletons (IServiceScopeFactory, registry, retry
        // policy, options, logger) and creates its own child scopes per batch/message.
        builder.Services.AddSingleton<IInboxProcessor, InboxProcessingPipeline>();
        builder.Services.AddHostedService<InboxProcessorService>();

        return builder;
    }

    /// <summary>
    /// Registers a background purge job that periodically deletes old processed/dead-lettered
    /// messages and old handler-attempt rows. Defaults retain 7 days of messages and 30 days
    /// of attempts; tune via <see cref="InboxPurgeOptions"/>.
    /// </summary>
    public static IInboxNetBuilder AddPurgeJob(
        this IInboxNetBuilder builder,
        Action<InboxPurgeOptions>? configure = null)
    {
        var optionsBuilder = builder.Services.AddOptions<InboxPurgeOptions>();
        if (configure is not null) optionsBuilder.Configure(configure);

        builder.Services.AddHostedService<InboxPurgeService>();
        return builder;
    }
}
