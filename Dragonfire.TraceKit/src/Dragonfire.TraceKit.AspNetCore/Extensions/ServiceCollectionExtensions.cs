using Dragonfire.TraceKit.AspNetCore.Http;
using Dragonfire.TraceKit.Extensions;
using Dragonfire.TraceKit.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;

namespace Dragonfire.TraceKit.AspNetCore.Extensions;

/// <summary>
/// Wires the core services <em>and</em> auto-attaches a <see cref="TraceKitDelegatingHandler"/>
/// to every <see cref="HttpClient"/> created by <see cref="IHttpClientFactory"/> — so
/// existing call sites need no changes.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers TraceKit core services and the global HttpClient handler filter.
    /// Call this once at startup, then call <c>app.UseTraceKit()</c> in the pipeline.
    /// </summary>
    public static ITraceKitBuilder AddTraceKitForAspNetCore(
        this IServiceCollection services,
        Action<TraceKitOptions>? configure = null)
    {
        var builder = services.AddTraceKit(configure);

        // Inserted via TryAddEnumerable so calling AddTraceKitForAspNetCore twice does
        // not register the filter twice.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHttpMessageHandlerBuilderFilter, TraceKitHandlerBuilderFilter>());

        return builder;
    }
}
