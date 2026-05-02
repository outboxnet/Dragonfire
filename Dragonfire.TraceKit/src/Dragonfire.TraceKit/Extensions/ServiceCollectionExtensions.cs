using Dragonfire.TraceKit.Abstractions;
using Dragonfire.TraceKit.Context;
using Dragonfire.TraceKit.Options;
using Dragonfire.TraceKit.Redaction;
using Dragonfire.TraceKit.Storage;
using Dragonfire.TraceKit.Writers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Dragonfire.TraceKit.Extensions;

/// <summary>
/// DI registration entry points for the core (framework-agnostic) TraceKit pieces:
/// options, redaction, ambient context, the bounded-channel writer, and the background
/// drain that calls <see cref="ITraceRepository"/>. Use
/// <c>Dragonfire.TraceKit.AspNetCore</c> to plug TraceKit into the HTTP pipeline.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the TraceKit core services. A no-op repository is registered by default
    /// — call <see cref="UseRepository{TRepository}"/> to plug in your own.
    /// </summary>
    public static ITraceKitBuilder AddTraceKit(
        this IServiceCollection services,
        Action<TraceKitOptions>? configure = null)
    {
        if (configure is not null)
            services.Configure(configure);
        else
            services.AddOptions<TraceKitOptions>();

        // Singletons: stateless, configured once, shared across requests.
        services.TryAddSingleton<ITraceRedactor, DefaultTraceRedactor>();
        services.TryAddSingleton<TraceContextAccessor>();
        services.TryAddSingleton<ITraceContextAccessor>(sp => sp.GetRequiredService<TraceContextAccessor>());
        services.TryAddSingleton<ChannelTraceWriter>();
        services.TryAddSingleton<ITraceWriter>(sp => sp.GetRequiredService<ChannelTraceWriter>());

        // Default repository — overridable.
        services.TryAddScoped<ITraceRepository, NullTraceRepository>();

        // Background drain.
        services.AddHostedService<TraceWriterHostedService>();

        return new TraceKitBuilder(services);
    }

    /// <summary>
    /// Replaces the default <see cref="NullTraceRepository"/> with a host-supplied
    /// implementation, scoped per-trace so EF Core / DbContext / Cosmos clients work
    /// without leaks.
    /// </summary>
    public static ITraceKitBuilder UseRepository<TRepository>(this ITraceKitBuilder builder)
        where TRepository : class, ITraceRepository
    {
        builder.Services.RemoveAll<ITraceRepository>();
        builder.Services.AddScoped<ITraceRepository, TRepository>();
        return builder;
    }

    /// <summary>
    /// Replaces the default redactor with a host-supplied implementation. Use this when
    /// the configurable lists in <see cref="RedactionOptions"/> are not enough — for
    /// example to redact based on tenant configuration or external policy.
    /// </summary>
    public static ITraceKitBuilder UseRedactor<TRedactor>(this ITraceKitBuilder builder)
        where TRedactor : class, ITraceRedactor
    {
        builder.Services.RemoveAll<ITraceRedactor>();
        builder.Services.AddSingleton<ITraceRedactor, TRedactor>();
        return builder;
    }
}
