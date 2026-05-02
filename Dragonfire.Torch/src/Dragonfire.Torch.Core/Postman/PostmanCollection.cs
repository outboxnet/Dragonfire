using System.Text.Json.Serialization;

namespace Dragonfire.Torch.Postman;

/// <summary>
/// Top-level Postman v2.1 collection JSON shape. Properties we don't care about
/// (auth at collection level, events, protocolProfileBehavior) are intentionally
/// not modelled — System.Text.Json silently ignores them.
/// </summary>
public sealed class PostmanCollection
{
    [JsonPropertyName("info")]
    public PostmanInfo? Info { get; set; }

    [JsonPropertyName("variable")]
    public List<PostmanVariable> Variable { get; set; } = new();

    [JsonPropertyName("item")]
    public List<PostmanItem> Item { get; set; } = new();
}

public sealed class PostmanInfo
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("schema")]
    public string? Schema { get; set; }
}

public sealed class PostmanVariable
{
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    [JsonPropertyName("value")]
    [JsonConverter(typeof(LooseStringConverter))]
    public string? Value { get; set; }
}
