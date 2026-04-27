using Microsoft.Extensions.DependencyInjection;
using OutboxNet.Extensions;
using OutboxNet.Interfaces;
using OutboxNet.Processor;

namespace OutboxNet.AzureFunctions.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the outbox processor for use in an Azure Functions app.
    /// <para>
    /// The timer schedule is read from the <c>Outbox:TimerCron</c> application setting.
    /// Add this to your <c>local.settings.json</c> under <c>Values</c> and to your
    /// Azure App Settings:
    /// <code>
    ///   "Outbox:TimerCron": "*/10 * * * * *"   // every 10 seconds
    /// </code>
    /// </para>
    /// </summary>
    public static IOutboxNetBuilder AddAzureFunctionsProcessor(this IOutboxNetBuilder builder)
    {
        // Singleton: the pipeline only takes singletons and manages its own child scopes.
        builder.Services.AddSingleton<IOutboxProcessor, OutboxProcessingPipeline>();
        return builder;
    }
}
