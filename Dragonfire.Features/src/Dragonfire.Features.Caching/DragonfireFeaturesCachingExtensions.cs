using System;
using System.Linq;
using Dragonfire.Caching.Interfaces;
using Dragonfire.Features.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dragonfire.Features.Caching;

/// <summary>
/// Adds the <see cref="CachingFeatureResolver"/> decorator. Call after <c>AddDragonfireFeatures()</c>
/// and after registering an <see cref="ICacheService"/> via the Dragonfire.Caching extensions.
///
/// <code>
/// builder.Services.AddDragonfireCaching().AddMemoryProvider();
/// builder.Services.AddDragonfireFeatures();
/// builder.Services.AddDragonfireFeaturesCaching(o =&gt; o.Ttl = TimeSpan.FromMinutes(1));
/// </code>
///
/// <para>Tenants needing live evaluation can flush their slice with:</para>
/// <code>
/// await cacheService.InvalidateByTagAsync($"features-tenant:{tenantId}");
/// </code>
/// </summary>
public static class DragonfireFeaturesCachingExtensions
{
    public static IServiceCollection AddDragonfireFeaturesCaching(
        this IServiceCollection services,
        Action<CachingFeatureResolverOptions>? configure = null)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));

        var options = new CachingFeatureResolverOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        // Find and remove the existing IFeatureResolver registration, capturing its descriptor
        // so the decorator can resolve the inner via DI. Falls back to DefaultFeatureResolver
        // if AddDragonfireFeatures() hasn't been called yet — emit a clearer error than NRE.
        var existing = services.FirstOrDefault(s => s.ServiceType == typeof(IFeatureResolver))
            ?? throw new InvalidOperationException(
                "Call AddDragonfireFeatures() before AddDragonfireFeaturesCaching().");

        services.Remove(existing);

        services.AddSingleton<DefaultFeatureResolver>();
        services.AddSingleton<IFeatureResolver>(sp =>
        {
            var inner = ResolveInner(sp, existing);
            var cache = sp.GetRequiredService<ICacheService>();
            var ctxAccessor = sp.GetRequiredService<IFeatureContextAccessor>();
            return new CachingFeatureResolver(inner, cache, ctxAccessor, options);
        });

        return services;
    }

    private static IFeatureResolver ResolveInner(IServiceProvider sp, ServiceDescriptor existing)
    {
        if (existing.ImplementationInstance is IFeatureResolver instance)
            return instance;

        if (existing.ImplementationFactory is { } factory)
            return (IFeatureResolver)factory(sp);

        if (existing.ImplementationType is { } implType)
        {
            // Use ActivatorUtilities so constructor parameters resolve from DI.
            return (IFeatureResolver)ActivatorUtilities.CreateInstance(sp, implType);
        }

        throw new InvalidOperationException(
            "Cannot resolve inner IFeatureResolver for caching decorator.");
    }
}
