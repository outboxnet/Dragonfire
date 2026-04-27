using Dragonfire.Caching.Interfaces;
using Google.Protobuf;

namespace Dragonfire.Caching.Serialization.Protobuf.Serializers;

/// <summary>
/// Cache serializer using Google Protocol Buffers.
/// Only supports types that implement <see cref="IMessage"/> (generated protobuf messages).
/// For all other types register and use <c>SystemTextJsonSerializer</c> instead.
/// </summary>
public sealed class ProtobufSerializer : ICacheSerializer
{
    public byte[] Serialize<T>(T value)
    {
        if (value is IMessage message)
            return message.ToByteArray();

        throw new NotSupportedException(
            $"ProtobufSerializer only supports {nameof(IMessage)} types. '{typeof(T)}' does not implement IMessage.");
    }

    public T Deserialize<T>(byte[] bytes)
    {
        if (!typeof(IMessage).IsAssignableFrom(typeof(T)))
        {
            throw new NotSupportedException(
                $"ProtobufSerializer only supports {nameof(IMessage)} types. '{typeof(T)}' does not implement IMessage.");
        }

        var parserProp = typeof(T).GetProperty("Parser",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        if (parserProp?.GetValue(null) is not { } parser)
            throw new InvalidOperationException($"Cannot find static 'Parser' property on '{typeof(T)}'.");

        var parseFrom = parser.GetType().GetMethod("ParseFrom", [typeof(byte[])]);
        if (parseFrom is null)
            throw new InvalidOperationException($"Cannot find 'ParseFrom(byte[])' on the parser for '{typeof(T)}'.");

        return (T)parseFrom.Invoke(parser, [bytes])!;
    }
}
