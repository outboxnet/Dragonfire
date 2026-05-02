using System.Text.Json.Serialization;

namespace Dragonfire.Spark.Postman;

/// <summary>
/// Postman v2.1 collection output model. Only the properties the converter
/// writes are modelled — readers of the generated file see a valid v2.1
/// collection that Postman and dragonfire-apigen can import.
/// </summary>
public sealed class PostmanCollection
{
    [JsonPropertyName("info")]
    public PostmanInfo Info { get; set; } = new();

    [JsonPropertyName("item")]
    public List<PostmanItem> Item { get; set; } = new();

    [JsonPropertyName("variable")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PostmanVariable>? Variable { get; set; }
}

public sealed class PostmanInfo
{
    [JsonPropertyName("_postman_id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Imported Collection";

    [JsonPropertyName("schema")]
    public string Schema { get; set; } =
        "https://schema.getpostman.com/json/collection/v2.1.0/collection.json";
}

/// <summary>
/// Represents either a folder (<see cref="Item"/> populated, <see cref="Request"/> null)
/// or a request (<see cref="Request"/> populated, <see cref="Item"/> null).
/// </summary>
public sealed class PostmanItem
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>Non-null for folders.</summary>
    [JsonPropertyName("item")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PostmanItem>? Item { get; set; }

    /// <summary>Non-null for request items.</summary>
    [JsonPropertyName("request")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PostmanRequest? Request { get; set; }

    /// <summary>Saved response examples attached to this request.</summary>
    [JsonPropertyName("response")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PostmanSavedResponse>? Response { get; set; }
}

public sealed class PostmanRequest
{
    [JsonPropertyName("method")]
    public string Method { get; set; } = "GET";

    [JsonPropertyName("header")]
    public List<PostmanHeader> Header { get; set; } = new();

    [JsonPropertyName("url")]
    public PostmanUrl Url { get; set; } = new();

    [JsonPropertyName("body")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PostmanBody? Body { get; set; }
}

public sealed class PostmanUrl
{
    [JsonPropertyName("raw")]
    public string Raw { get; set; } = "";

    [JsonPropertyName("protocol")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Protocol { get; set; }

    [JsonPropertyName("host")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Host { get; set; }

    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Path { get; set; }

    [JsonPropertyName("query")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PostmanQueryParam>? Query { get; set; }
}

public sealed class PostmanQueryParam
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    [JsonPropertyName("disabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Disabled { get; set; }
}

public sealed class PostmanHeader
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    [JsonPropertyName("disabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Disabled { get; set; }
}

public sealed class PostmanBody
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "raw";

    /// <summary>Used when <see cref="Mode"/> is "raw".</summary>
    [JsonPropertyName("raw")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Raw { get; set; }

    /// <summary>Used when <see cref="Mode"/> is "urlencoded".</summary>
    [JsonPropertyName("urlencoded")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PostmanFormParam>? UrlEncoded { get; set; }

    /// <summary>Used when <see cref="Mode"/> is "formdata".</summary>
    [JsonPropertyName("formdata")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PostmanFormParam>? FormData { get; set; }

    [JsonPropertyName("options")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PostmanBodyOptions? Options { get; set; }
}

public sealed class PostmanBodyOptions
{
    [JsonPropertyName("raw")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PostmanRawOptions? Raw { get; set; }
}

public sealed class PostmanRawOptions
{
    [JsonPropertyName("language")]
    public string Language { get; set; } = "json";
}

public sealed class PostmanFormParam
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("value")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Value { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("src")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Src { get; set; }
}

public sealed class PostmanSavedResponse
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("originalRequest")]
    public PostmanRequest OriginalRequest { get; set; } = new();

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("header")]
    public List<PostmanHeader> Header { get; set; } = new();

    [JsonPropertyName("body")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Body { get; set; }
}

public sealed class PostmanVariable
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";
}
