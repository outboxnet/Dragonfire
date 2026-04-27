using Dragonfire.Caching.Extensions;
using Dragonfire.Caching.Interfaces;
using Dragonfire.Caching.Memory.Providers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dragonfire.Caching.Memory.Extensions;

/// <summary>
/// Registers the in-memory cache provider for Dragonfire.Caching.
/// </summary>
public static class DragonfireMemoryExtensions
{
    /// <summary>
    /// Add the in-memory <see cref="ICacheProvider"/> and all core Dragonfire services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureMemory">Optional delegate to configure <see cref="MemoryCacheOptions"/>.</param>
    /// <param name="configureCaching">Optional delegate to further configure caching services.</param>
    public static IServiceCollection AddDragonfireMemoryCache(
        this IServiceCollection services,
        Action<MemoryCacheOptions>? configureMemory = null,
        Action<DragonfireCachingBuilder>? configureCaching = null)
    {
        if (configureMemory is not null)
            services.AddMemoryCache(configureMemory);
        else
            services.AddMemoryCache();

        services.TryAddSingleton<ICacheProvider, MemoryCacheProvider>();

        services.AddDragonfireCaching(configureCaching);

        return services;
    }
}
