using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dragonfire.Torch.Postman;

public sealed class PostmanRequest
{
    [JsonPropertyName("method")]
    public string? Method { get; set; }

    [JsonPropertyName("header")]
    public List<PostmanHeader> Header { get; set; } = new();

    /// <summary>
    /// Postman serialises <c>url</c> two different ways: a bare string (rare,
    /// older exports) or a structured <see cref="PostmanUrl"/>. We accept both
    /// and surface the structured form via <see cref="UrlObject"/>.
    /// </summary>
    [JsonPropertyName("url")]
    public JsonElement? Url { get; set; }

    [JsonPropertyName("body")]
    public PostmanBody? Body { get; set; }

    [JsonIgnore]
    public PostmanUrl? UrlObject
    {
        get
        {
            if (Url is null) return null;
            var el = Url.Value;
            return el.ValueKind switch
            {
                JsonValueKind.String => new PostmanUrl { Raw = el.GetString() },
                JsonValueKind.Object => el.Deserialize<PostmanUrl>(),
                _ => null,
            };
        }
    }
}

public sealed class PostmanHeader
{
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    [JsonPropertyName("value")]
    [JsonConverter(typeof(LooseStringConverter))]
    public string? Value { get; set; }

    [JsonPropertyName("disabled")]
    public bool Disabled { get; set; }
}

public sealed class PostmanUrl
{
    [JsonPropertyName("raw")]
    public string? Raw { get; set; }

    [JsonPropertyName("host")]
    public List<string> Host { get; set; } = new();

    [JsonPropertyName("path")]
    public List<JsonElement> Path { get; set; } = new();

    [JsonPropertyName("query")]
    public List<PostmanQueryParam> Query { get; set; } = new();

    [JsonPropertyName("variable")]
    public List<PostmanPathVariable> Variable { get; set; } = new();

    /// <summary>
    /// Postman occasionally serialises path segments as objects (with a "value"
    /// property) instead of plain strings. Coerce both forms into a string list.
    /// </summary>
    public IEnumerable<string> PathSegments()
    {
        foreach (var el in Path)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.String:
                    yield return el.GetString() ?? "";
                    break;
                case JsonValueKind.Object when el.TryGetProperty("value", out var v):
                    yield return v.GetString() ?? "";
                    break;
            }
        }
    }
}

public sealed class PostmanQueryParam
{
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    [JsonPropertyName("value")]
    [JsonConverter(typeof(LooseStringConverter))]
    public string? Value { get; set; }

    [JsonPropertyName("disabled")]
    public bool Disabled { get; set; }
}

public sealed class PostmanPathVariable
{
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    [JsonPropertyName("value")]
    [JsonConverter(typeof(LooseStringConverter))]
    public string? Value { get; set; }
}

public sealed class PostmanBody
{
    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    [JsonPropertyName("raw")]
    public string? Raw { get; set; }

    [JsonPropertyName("urlencoded")]
    public List<PostmanFormField> UrlEncoded { get; set; } = new();

    [JsonPropertyName("formdata")]
    public List<PostmanFormField> FormData { get; set; } = new();

    [JsonPropertyName("options")]
    public JsonElement? Options { get; set; }
}

public sealed class PostmanFormField
{
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    [JsonPropertyName("value")]
    [JsonConverter(typeof(LooseStringConverter))]
    public string? Value { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}
