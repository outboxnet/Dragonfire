using Dragonfire.Caching.Extensions;
using Dragonfire.Caching.Interfaces;
using Dragonfire.Caching.Serialization.Protobuf.Serializers;
using Microsoft.Extensions.DependencyInjection;

namespace Dragonfire.Caching.Serialization.Protobuf.Extensions;

/// <summary>
/// Extension methods for registering the Protobuf serializer with Dragonfire.Caching.
/// </summary>
public static class DragonfireProtobufExtensions
{
    /// <summary>
    /// Replace the default JSON serializer with <see cref="ProtobufSerializer"/>.
    /// Only effective for types implementing <c>Google.Protobuf.IMessage</c>.
    /// </summary>
    public static DragonfireCachingBuilder UseProtobufSerializer(this DragonfireCachingBuilder builder)
    {
        builder.UseSerializer<ProtobufSerializer>();
        return builder;
    }

    /// <summary>
    /// Register the Protobuf serializer directly on the service collection.
    /// </summary>
    public static IServiceCollection AddDragonfireProtobufSerializer(this IServiceCollection services)
    {
        services.AddSingleton<ICacheSerializer, ProtobufSerializer>();
        return services;
    }
}
