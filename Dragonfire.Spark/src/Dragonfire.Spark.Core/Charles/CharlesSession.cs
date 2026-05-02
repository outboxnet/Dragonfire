using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dragonfire.Spark.Charles;

/// <summary>
/// Root of a Charles Proxy JSON export. Charles serialises its session tree
/// as a recursive structure: a root object has a <c>sessions</c> array whose
/// elements are either sub-sessions (with their own <c>sessions</c>) or leaf
/// groups (with a <c>transactions</c> array). The parser flattens the tree
/// into a folder-preserving list.
/// </summary>
public sealed class CharlesRoot
{
    /// <summary>
    /// Top-level name (the recording name shown in Charles).
    /// May be null on older exports.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("sessions")]
    public List<CharlesSession> Sessions { get; set; } = new();
}

public sealed class CharlesSession
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Nested groups — mirrors Charles' tree view.</summary>
    [JsonPropertyName("sessions")]
    public List<CharlesSession> Sessions { get; set; } = new();

    [JsonPropertyName("transactions")]
    public List<CharlesTransaction> Transactions { get; set; } = new();
}

public sealed class CharlesTransaction
{
    [JsonPropertyName("request")]
    public CharlesRequest? Request { get; set; }

    [JsonPropertyName("response")]
    public CharlesResponse? Response { get; set; }
}

public sealed class CharlesRequest
{
    [JsonPropertyName("method")]
    public string? Method { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>Charles wraps headers in an object with a "headers" array.</summary>
    [JsonPropertyName("headers")]
    public CharlesHeaderBag? Headers { get; set; }

    [JsonPropertyName("body")]
    public CharlesBody? Body { get; set; }
}

public sealed class CharlesResponse
{
    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("headers")]
    public CharlesHeaderBag? Headers { get; set; }

    [JsonPropertyName("body")]
    public CharlesBody? Body { get; set; }
}

/// <summary>
/// Charles wraps its header list in <c>{ "headers": [...] }</c> to allow
/// duplicate header names (HTTP spec permits this).
/// </summary>
public sealed class CharlesHeaderBag
{
    [JsonPropertyName("headers")]
    public List<CharlesHeader> Headers { get; set; } = new();
}

public sealed class CharlesHeader
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("value")]
    [JsonConverter(typeof(CharlesLooseStringConverter))]
    public string? Value { get; set; }
}

public sealed class CharlesBody
{
    [JsonPropertyName("mime")]
    public string? Mime { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    /// <summary>
    /// Charles sometimes encodes binary bodies as base64 in a "body" field
    /// rather than "text".
    /// </summary>
    [JsonPropertyName("body")]
    public string? Base64Body { get; set; }

    [JsonPropertyName("charset")]
    public string? Charset { get; set; }

    [JsonPropertyName("contentEncoding")]
    public string? ContentEncoding { get; set; }

    /// <summary>True when the body bytes were base64-encoded by Charles.</summary>
    [JsonIgnore]
    public bool IsBinary => !string.IsNullOrEmpty(Base64Body) && string.IsNullOrEmpty(Text);
}

internal sealed class CharlesLooseStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:   return null;
            case JsonTokenType.String: return reader.GetString();
            case JsonTokenType.True:   return "true";
            case JsonTokenType.False:  return "false";
            default:
                using (var doc = JsonDocument.ParseValue(ref reader))
                    return doc.RootElement.GetRawText();
        }
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value);
    }
}
