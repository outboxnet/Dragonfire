using Dragonfire.Caching.Core;
using Dragonfire.Caching.Interfaces;
using Dragonfire.Caching.Models;
using Dragonfire.Caching.Serializers;
using Dragonfire.Caching.Strategies;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dragonfire.Caching.Extensions;

/// <summary>
/// Extension methods for registering Dragonfire.Caching core services.
/// </summary>
public static class DragonfireCachingExtensions
{
    /// <summary>
    /// Registers the core Dragonfire Caching services.
    /// Call a provider extension (e.g. <c>AddDragonfireMemoryCache()</c>) separately to
    /// register an <see cref="ICacheProvider"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional delegate to configure <see cref="DragonfireCachingBuilder"/>.</param>
    public static IServiceCollection AddDragonfireCaching(
        this IServiceCollection services,
        Action<DragonfireCachingBuilder>? configure = null)
    {
        // Serializer
        services.TryAddSingleton<ICacheSerializer, SystemTextJsonSerializer>();

        // Key strategy
        services.TryAddSingleton<ICacheKeyStrategy, DefaultCacheKeyStrategy>();

        // Core internals
        services.TryAddSingleton<ITemplateResolver, TemplateResolver>();
        services.TryAddSingleton<ITagIndex, InMemoryTagIndex>();
        services.TryAddSingleton<CacheLockManager>();
        services.TryAddSingleton<CacheMetrics>();

        // High-level service
        services.TryAddSingleton<ICacheService, CacheService>();

        // Configuration-driven executor
        services.TryAddSingleton<CacheExecutor>();

        var builder = new DragonfireCachingBuilder(services);
        configure?.Invoke(builder);

        return services;
    }

    /// <summary>
    /// Registers the core services and binds <see cref="CacheSettings"/> from
    /// <paramref name="configuration"/> section <c>Caching</c> (override with
    /// <paramref name="sectionName"/>).
    /// Also enables the <see cref="InvalidationWorker"/> background service.
    /// </summary>
    public static IServiceCollection AddDragonfireCaching(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<DragonfireCachingBuilder>? configure = null,
        string sectionName = "Caching")
    {
        services.Configure<CacheSettings>(configuration.GetSection(sectionName));

        services.AddDragonfireCaching(builder =>
        {
            builder.UseQueuedInvalidation();
            configure?.Invoke(builder);
        });

        return services;
    }

    /// <summary>
    /// Register a custom <see cref="ICacheProvider"/> as the backing store.
    /// </summary>
    public static IServiceCollection AddDragonfireCacheProvider<TProvider>(this IServiceCollection services)
        where TProvider : class, ICacheProvider
    {
        services.AddSingleton<ICacheProvider, TProvider>();
        return services;
    }
}
