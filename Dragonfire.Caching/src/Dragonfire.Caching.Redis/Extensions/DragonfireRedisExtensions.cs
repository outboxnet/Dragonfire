using Dragonfire.Caching.Extensions;
using Dragonfire.Caching.Interfaces;
using Dragonfire.Caching.Redis.Core;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Dragonfire.Caching.Redis.Extensions;

/// <summary>
/// Extension methods for registering Redis-backed Dragonfire.Caching components.
/// </summary>
public static class DragonfireRedisExtensions
{
    /// <summary>
    /// Replace the in-memory tag index with <see cref="RedisTagIndex"/>.
    /// Requires a registered <see cref="IConnectionMultiplexer"/>.
    /// </summary>
    public static DragonfireCachingBuilder UseRedisTagIndex(this DragonfireCachingBuilder builder)
    {
        builder.UseTagIndex<RedisTagIndex>();
        return builder;
    }

    /// <summary>
    /// Register a <see cref="IConnectionMultiplexer"/> and switch to the Redis tag index.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">Redis connection string (e.g. <c>localhost:6379</c>).</param>
    public static IServiceCollection AddDragonfireRedisTagIndex(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddSingleton<IConnectionMultiplexer>(
            _ => ConnectionMultiplexer.Connect(connectionString));

        services.AddSingleton<ITagIndex, RedisTagIndex>();
        return services;
    }

    /// <summary>
    /// Register the Redis tag index using an already-registered <see cref="IConnectionMultiplexer"/>.
    /// </summary>
    public static IServiceCollection AddDragonfireRedisTagIndex(this IServiceCollection services)
    {
        services.AddSingleton<ITagIndex, RedisTagIndex>();
        return services;
    }
}
