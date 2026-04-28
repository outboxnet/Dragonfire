using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dragonfire.Features.AspNetCore;

/// <summary>
/// DI + endpoint extensions for the AspNetCore Features integration.
/// </summary>
public static class DragonfireFeaturesAspNetCoreExtensions
{
    /// <summary>
    /// Registers <see cref="HttpContextFeatureContextAccessor"/> (replacing the default
    /// no-context accessor) plus the action and endpoint filters. Call after
    /// <c>AddDragonfireFeatures()</c>.
    /// </summary>
    public static IServiceCollection AddDragonfireFeaturesAspNetCore(
        this IServiceCollection services,
        Action<FeatureAspNetCoreOptions>? configure = null)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));

        if (configure is not null)
            services.Configure(configure);
        else
            services.AddOptions<FeatureAspNetCoreOptions>();

        services.AddHttpContextAccessor();

        // Replace the default no-context accessor with the HttpContext-aware one.
        services.RemoveAll<IFeatureContextAccessor>();
        services.AddSingleton<IFeatureContextAccessor, HttpContextFeatureContextAccessor>();

        services.TryAddScoped<FeatureGateActionFilter>();

        return services;
    }

    /// <summary>
    /// Adds a <see cref="FeatureGateEndpointFilter"/> to a minimal-API endpoint. The endpoint
    /// returns the supplied <paramref name="deniedStatusCode"/> (default 404) when the feature
    /// is disabled.
    /// </summary>
    public static TBuilder RequireFeature<TBuilder>(
        this TBuilder builder,
        string featureName,
        int deniedStatusCode = 404)
        where TBuilder : IEndpointConventionBuilder
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        if (string.IsNullOrWhiteSpace(featureName))
            throw new ArgumentException("Feature name required.", nameof(featureName));

        builder.AddEndpointFilterFactory((factoryContext, next) =>
        {
            var resolver = factoryContext.ApplicationServices.GetRequiredService<IFeatureResolver>();
            var filter   = new FeatureGateEndpointFilter(resolver, featureName, deniedStatusCode);
            return invocation => filter.InvokeAsync(invocation, next);
        });
        return builder;
    }
}
