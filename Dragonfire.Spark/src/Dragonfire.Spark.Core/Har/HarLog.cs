using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dragonfire.Spark.Har;

public sealed class HarFile
{
    [JsonPropertyName("log")]
    public HarLog Log { get; set; } = new();
}

public sealed class HarLog
{
    [JsonPropertyName("entries")]
    public List<HarEntry> Entries { get; set; } = new();
}

public sealed class HarEntry
{
    [JsonPropertyName("request")]
    public HarRequest? Request { get; set; }

    [JsonPropertyName("response")]
    public HarResponse? Response { get; set; }
}

public sealed class HarRequest
{
    [JsonPropertyName("method")]
    public string? Method { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("headers")]
    public List<HarNameValue> Headers { get; set; } = new();

    [JsonPropertyName("queryString")]
    public List<HarNameValue> QueryString { get; set; } = new();

    [JsonPropertyName("postData")]
    public HarPostData? PostData { get; set; }
}

public sealed class HarResponse
{
    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("statusText")]
    public string? StatusText { get; set; }

    [JsonPropertyName("headers")]
    public List<HarNameValue> Headers { get; set; } = new();

    [JsonPropertyName("content")]
    public HarContent? Content { get; set; }
}

public sealed class HarNameValue
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("value")]
    [JsonConverter(typeof(LooseStringConverter))]
    public string? Value { get; set; }
}

public sealed class HarPostData
{
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("params")]
    public List<HarParam> Params { get; set; } = new();
}

public sealed class HarParam
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("value")]
    [JsonConverter(typeof(LooseStringConverter))]
    public string? Value { get; set; }

    [JsonPropertyName("fileName")]
    public string? FileName { get; set; }

    [JsonPropertyName("contentType")]
    public string? ContentType { get; set; }
}

public sealed class HarContent
{
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("encoding")]
    public string? Encoding { get; set; }
}

/// <summary>
/// Tolerant string converter: accepts numeric / boolean JSON values where a
/// string is expected (common in real-world HAR exports from some browsers).
/// </summary>
internal sealed class LooseStringConverter : JsonConverter<string?>
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
