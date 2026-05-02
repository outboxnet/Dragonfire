using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dragonfire.Torch.Inference;

/// <summary>
/// Infers a C# primitive type for a single JSON scalar value. Strings are
/// sniffed for ISO-8601 timestamps and Guid-shaped hex.
/// </summary>
public static class PrimitiveSniffer
{
    private static readonly Regex GuidRegex = new(
        @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$",
        RegexOptions.Compiled);

    public static string SniffScalar(JsonElement element, bool floatsAsDouble)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.True:
            case JsonValueKind.False:
                return "bool";

            case JsonValueKind.Number:
                if (element.TryGetInt32(out _)) return "int";
                if (element.TryGetInt64(out _)) return "long";
                return floatsAsDouble ? "double" : "decimal";

            case JsonValueKind.String:
                var s = element.GetString() ?? "";
                if (LooksLikeIso8601(s)) return "DateTimeOffset";
                if (GuidRegex.IsMatch(s)) return "Guid";
                return "string";

            case JsonValueKind.Null:
                // Unresolved on its own; the caller decides based on peer values.
                return "string";

            default:
                return "string";
        }
    }

    private static bool LooksLikeIso8601(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        if (s.Length < 10) return false;
        // Cheap pre-filter: digits in YYYY-MM-DD positions.
        if (!(char.IsDigit(s[0]) && char.IsDigit(s[1]) && char.IsDigit(s[2]) && char.IsDigit(s[3]) && s[4] == '-'))
            return false;

        return DateTimeOffset.TryParse(
            s,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind | DateTimeStyles.AssumeUniversal,
            out _);
    }
}
