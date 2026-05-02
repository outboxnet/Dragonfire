using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dragonfire.Torch.Har;

/// <summary>
/// HAR files captured from browsers sometimes contain numeric header/param
/// values (e.g. Content-Length). Accepts any primitive JSON token and
/// stringifies it so parsing stays tolerant of real-world HAR exports.
/// </summary>
public sealed class HarLooseStringConverter : JsonConverter<string?>
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
