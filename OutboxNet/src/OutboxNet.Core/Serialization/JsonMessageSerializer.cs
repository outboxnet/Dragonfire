using System.Text.Json;
using OutboxNet.Interfaces;

namespace OutboxNet.Serialization;

public class JsonMessageSerializer : IMessageSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public string Serialize(object payload) => JsonSerializer.Serialize(payload, Options);

    public T? Deserialize<T>(string payload) => JsonSerializer.Deserialize<T>(payload, Options);
}
