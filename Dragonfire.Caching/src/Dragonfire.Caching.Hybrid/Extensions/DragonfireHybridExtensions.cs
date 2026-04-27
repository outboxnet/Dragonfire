using Dragonfire.Caching.Distributed.Providers;
using Dragonfire.Caching.Extensions;
using Dragonfire.Caching.Hybrid.Providers;
using Dragonfire.Caching.Interfaces;
using Dragonfire.Caching.Memory.Providers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dragonfire.Caching.Hybrid.Extensions;

/// <summary>
/// Registers the hybrid (L1 memory + L2 distributed) cache provider for Dragonfire.Caching.
/// </summary>
public static class DragonfireHybridExtensions
{
    /// <summary>
    /// Add the hybrid <see cref="ICacheProvider"/> (L1 = in-process memory, L2 = distributed).
    /// You must register an <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/>
    /// separately (e.g. <c>services.AddStackExchangeRedisCache(...)</c>).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureMemory">Optional memory cache options (L1).</param>
    /// <param name="configureCaching">Optional delegate to further configure caching services.</param>
    public static IServiceCollection AddDragonfireHybridCache(
        this IServiceCollection services,
        Action<MemoryCacheOptions>? configureMemory = null,
        Action<DragonfireCachingBuilder>? configureCaching = null)
    {
        if (configureMemory is not null)
            services.AddMemoryCache(configureMemory);
        else
            services.AddMemoryCache();

        // Register concrete providers under their own type + keyed
        services.TryAddSingleton<MemoryCacheProvider>();
        services.TryAddSingleton<DistributedCacheProvider>();

        services.AddKeyedSingleton<ICacheProvider, MemoryCacheProvider>(HybridCacheKeys.L1,
            (sp, _) => sp.GetRequiredService<MemoryCacheProvider>());

        services.AddKeyedSingleton<ICacheProvider, DistributedCacheProvider>(HybridCacheKeys.L2,
            (sp, _) => sp.GetRequiredService<DistributedCacheProvider>());

        services.TryAddSingleton<ICacheProvider, HybridCacheProvider>();

        services.AddDragonfireCaching(configureCaching);

        return services;
    }

    /// <summary>
    /// Add the hybrid provider using the in-memory distributed cache for L2
    /// (useful for development / single-node scenarios).
    /// </summary>
    public static IServiceCollection AddDragonfireHybridMemoryCache(
        this IServiceCollection services,
        Action<MemoryCacheOptions>? configureL1 = null,
        Action<DragonfireCachingBuilder>? configureCaching = null)
    {
        services.AddDistributedMemoryCache();
        return services.AddDragonfireHybridCache(configureL1, configureCaching);
    }
}
