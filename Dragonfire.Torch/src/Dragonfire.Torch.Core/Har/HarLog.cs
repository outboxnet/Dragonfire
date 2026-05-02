using System.Text.Json.Serialization;

namespace Dragonfire.Torch.Har;

/// <summary>
/// Minimal HAR (HTTP Archive 1.2) object model — only the properties the
/// generator needs. Unrecognised fields are silently ignored by STJ.
/// Spec: https://w3c.github.io/web-performance/specs/HAR/Overview.html
/// </summary>
public sealed class HarFile
{
    [JsonPropertyName("log")]
    public HarLog Log { get; set; } = new();
}

public sealed class HarLog
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("entries")]
    public List<HarEntry> Entries { get; set; } = new();
}

public sealed class HarEntry
{
    [JsonPropertyName("startedDateTime")]
    public string? StartedDateTime { get; set; }

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
    public List<HarHeader> Headers { get; set; } = new();

    [JsonPropertyName("queryString")]
    public List<HarQueryParam> QueryString { get; set; } = new();

    [JsonPropertyName("postData")]
    public HarPostData? PostData { get; set; }
}

public sealed class HarResponse
{
    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("headers")]
    public List<HarHeader> Headers { get; set; } = new();

    [JsonPropertyName("content")]
    public HarContent? Content { get; set; }
}

public sealed class HarHeader
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("value")]
    [JsonConverter(typeof(HarLooseStringConverter))]
    public string? Value { get; set; }
}

public sealed class HarQueryParam
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("value")]
    [JsonConverter(typeof(HarLooseStringConverter))]
    public string? Value { get; set; }
}

public sealed class HarPostData
{
    /// <summary>MIME type, e.g. "application/json", "application/x-www-form-urlencoded", "multipart/form-data".</summary>
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    /// <summary>Raw body text (used for JSON bodies and urlencoded strings).</summary>
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    /// <summary>Structured form params (some browsers populate this instead of <c>text</c>).</summary>
    [JsonPropertyName("params")]
    public List<HarParam> Params { get; set; } = new();
}

public sealed class HarParam
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("value")]
    [JsonConverter(typeof(HarLooseStringConverter))]
    public string? Value { get; set; }

    /// <summary>"file" when the param is a file upload.</summary>
    [JsonPropertyName("contentType")]
    public string? ContentType { get; set; }

    [JsonPropertyName("fileName")]
    public string? FileName { get; set; }
}

public sealed class HarContent
{
    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    /// <summary>Base64 encoding used when content is binary. The generator ignores binary responses.</summary>
    [JsonPropertyName("encoding")]
    public string? Encoding { get; set; }
}
