using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dragonfire.ApiClientGen.Postman;

/// <summary>
/// Postman exports are not schema-strict: header / query / variable values
/// occasionally come through as numbers or booleans (e.g. <c>"value": 1234</c>
/// for <c>Content-Length</c>). The default <see cref="string"/> converter
/// throws on those. This converter accepts any primitive token and stringifies
/// it, so the parser stays tolerant of real-world exports.
/// </summary>
public sealed class LooseStringConverter : JsonConverter<string?>
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
