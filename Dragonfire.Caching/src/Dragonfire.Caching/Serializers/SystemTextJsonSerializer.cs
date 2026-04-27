using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dragonfire.Caching.Interfaces;

namespace Dragonfire.Caching.Serializers;

/// <summary>
/// Default cache serializer using System.Text.Json.
/// Pass custom <see cref="JsonSerializerOptions"/> to change the serialisation behaviour.
/// </summary>
public sealed class SystemTextJsonSerializer : ICacheSerializer
{
    public static readonly JsonSerializerOptions DefaultOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly JsonSerializerOptions _options;

    public SystemTextJsonSerializer() : this(DefaultOptions) { }

    public SystemTextJsonSerializer(JsonSerializerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public byte[] Serialize<T>(T value)
        => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, _options));

    public T Deserialize<T>(byte[] bytes)
        => JsonSerializer.Deserialize<T>(Encoding.UTF8.GetString(bytes), _options)!;
}
