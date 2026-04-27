using Dragonfire.Caching.Distributed.Providers;
using Dragonfire.Caching.Extensions;
using Dragonfire.Caching.Interfaces;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dragonfire.Caching.Distributed.Extensions;

/// <summary>
/// Registers the <see cref="IDistributedCache"/>-backed provider for Dragonfire.Caching.
/// </summary>
public static class DragonfireDistributedExtensions
{
    /// <summary>
    /// Add the distributed <see cref="ICacheProvider"/> and all core Dragonfire services.
    /// You must register an <see cref="IDistributedCache"/> implementation separately
    /// (e.g. <c>services.AddStackExchangeRedisCache(...)</c>).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureCaching">Optional delegate to further configure caching services.</param>
    public static IServiceCollection AddDragonfireDistributedCache(
        this IServiceCollection services,
        Action<DragonfireCachingBuilder>? configureCaching = null)
    {
        services.TryAddSingleton<ICacheProvider, DistributedCacheProvider>();
        services.AddDragonfireCaching(configureCaching);
        return services;
    }

    /// <summary>
    /// Add the distributed provider using the built-in in-memory distributed cache
    /// (useful for development / single-node testing).
    /// </summary>
    public static IServiceCollection AddDragonfireDistributedMemoryCache(
        this IServiceCollection services,
        Action<DragonfireCachingBuilder>? configureCaching = null)
    {
        services.AddDistributedMemoryCache();
        return services.AddDragonfireDistributedCache(configureCaching);
    }
}
