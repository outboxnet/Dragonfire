using Dragonfire.Caching.Core;
using Dragonfire.Caching.Interfaces;
using Dragonfire.Caching.Models;
using Dragonfire.Caching.Serializers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Text.Json;

namespace Dragonfire.Caching.Extensions;

/// <summary>
/// Fluent builder for configuring Dragonfire Caching.
/// Obtain an instance via <see cref="DragonfireCachingExtensions.AddDragonfireCaching"/>.
/// </summary>
public sealed class DragonfireCachingBuilder
{
    public IServiceCollection Services { get; }

    internal DragonfireCachingBuilder(IServiceCollection services)
    {
        Services = services;
    }

    /// <summary>Replace the default JSON serializer with a custom <see cref="ICacheSerializer"/>.</summary>
    public DragonfireCachingBuilder UseSerializer<TSerializer>()
        where TSerializer : class, ICacheSerializer
    {
        Services.Replace(ServiceDescriptor.Singleton<ICacheSerializer, TSerializer>());
        return this;
    }

    /// <summary>Replace the default JSON serializer with custom options.</summary>
    public DragonfireCachingBuilder UseJsonSerializer(JsonSerializerOptions options)
    {
        Services.Replace(ServiceDescriptor.Singleton<ICacheSerializer>(
            new SystemTextJsonSerializer(options)));
        return this;
    }

    /// <summary>
    /// Enable the channel-based invalidation queue + background worker.
    /// Required when using <see cref="Core.CacheExecutor.ExecuteAndInvalidateAsync"/>.
    /// </summary>
    public DragonfireCachingBuilder UseQueuedInvalidation(Action<InvalidationQueueOptions>? configure = null)
    {
        if (configure is not null)
            Services.Configure(configure);
        else
            Services.TryAddSingleton(Microsoft.Extensions.Options.Options.Create(new InvalidationQueueOptions()));

        Services.TryAddSingleton<ChannelInvalidationQueue>();
        Services.TryAddSingleton<IInvalidationQueue>(sp => sp.GetRequiredService<ChannelInvalidationQueue>());
        Services.AddHostedService<InvalidationWorker>();
        return this;
    }

    /// <summary>
    /// Override the in-memory tag index with a distributed implementation (e.g. Redis).
    /// </summary>
    public DragonfireCachingBuilder UseTagIndex<TTagIndex>()
        where TTagIndex : class, ITagIndex
    {
        Services.Replace(ServiceDescriptor.Singleton<ITagIndex, TTagIndex>());
        return this;
    }
}
