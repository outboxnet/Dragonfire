using System.Text.Json.Serialization;

namespace Dragonfire.Torch.Postman;

/// <summary>
/// A Postman item is either a request (has <see cref="Request"/>) or a folder
/// (has <see cref="Item"/> with nested children). Folders can be arbitrarily deep.
/// </summary>
public sealed class PostmanItem
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("request")]
    public PostmanRequest? Request { get; set; }

    [JsonPropertyName("response")]
    public List<PostmanResponse> Response { get; set; } = new();

    [JsonPropertyName("item")]
    public List<PostmanItem>? Item { get; set; }

    [JsonIgnore]
    public bool IsFolder => Item is { Count: > 0 } && Request is null;
}
