using Microsoft.Extensions.DependencyInjection;
using InboxNet.Extensions;
using InboxNet.Interfaces;
using InboxNet.Processor;

namespace InboxNet.AzureFunctions.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the InboxNet pipeline for Azure Functions Isolated Worker.
    /// <para>
    /// Unlike <c>AddBackgroundDispatcher()</c>, this does <b>not</b> register a
    /// <c>BackgroundService</c>. Dispatch is driven entirely by your timer trigger function
    /// calling <see cref="InboxFunctionBase.DispatchAsync"/>, which is correct for both
    /// Consumption and Premium/Dedicated plans.
    /// </para>
    /// <para>
    /// Usage in <c>Program.cs</c>:
    /// <code>
    /// var host = new HostBuilder()
    ///     .ConfigureFunctionsWorkerDefaults()
    ///     .ConfigureServices(services =>
    ///     {
    ///         services
    ///             .AddInboxNet(o => { o.SchemaName = "inbox"; o.BatchSize = 50; })
    ///             .UseSqlServer(connectionString)
    ///             .AddAzureFunctions()
    ///             .AddStripeProvider(o => o.SigningSecret = "whsec_...")
    ///             .AddHandler&lt;OrderPaidHandler&gt;(h => h
    ///                 .ForProvider("stripe")
    ///                 .ForEvent("invoice.paid"));
    ///     })
    ///     .Build();
    /// </code>
    /// </para>
    /// <para>
    /// Then create one class inheriting <see cref="InboxFunctionBase"/> in your function app
    /// to set your own HTTP route and timer cron. See <see cref="InboxFunctionBase"/> for the
    /// full example.
    /// </para>
    /// </summary>
    public static IInboxNetBuilder AddAzureFunctions(this IInboxNetBuilder builder)
    {
        // Singleton: InboxProcessingPipeline manages its own child scopes via
        // IServiceScopeFactory. No BackgroundService — timer trigger drives dispatch.
        builder.Services.AddSingleton<IInboxProcessor, InboxProcessingPipeline>();

        // Scoped: resolved per function invocation, injects scoped IInboxPublisher and
        // IWebhookProviderRegistry from the invocation scope directly.
        builder.Services.AddScoped<IInboxFunctionHandler, InboxFunctionHandler>();

        return builder;
    }
}
