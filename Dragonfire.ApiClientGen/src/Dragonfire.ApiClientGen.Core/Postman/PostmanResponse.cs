using System.Text.Json.Serialization;

namespace Dragonfire.ApiClientGen.Postman;

public sealed class PostmanResponse
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("code")]
    public int? Code { get; set; }

    [JsonPropertyName("header")]
    public List<PostmanHeader> Header { get; set; } = new();

    [JsonPropertyName("body")]
    public string? Body { get; set; }
}
