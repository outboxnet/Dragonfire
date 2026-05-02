using System.Text.Json;

namespace Dragonfire.TraceKit.SampleApp.Views;

/// <summary>
/// Renders an arbitrary JSON document as a flat list of <c>(path, value)</c> rows so the
/// trace viewer can show a body as a dictionary table instead of a raw JSON blob.
/// Path syntax: <c>user.address.city</c>, <c>items[0]</c>, <c>items[1].id</c>.
/// </summary>
public static class JsonFlattener
{
    private const string TruncationMarker = " \u2026 [truncated";  // " … [truncated …]"

    /// <summary>Tries to parse <paramref name="body"/> as JSON and flatten it.</summary>
    public static bool TryFlatten(
        string? body,
        string? contentType,
        out List<KeyValuePair<string, string>> rows)
    {
        rows = new List<KeyValuePair<string, string>>();
        if (string.IsNullOrWhiteSpace(body)) return false;

        // TraceKit appends a "… [truncated, captured first N bytes]" suffix when a body
        // is cut at MaxBodyBytes — strip it before parsing or the JsonDocument call fails.
        var cut = body.IndexOf(TruncationMarker, StringComparison.Ordinal);
        var json = cut >= 0 ? body[..cut] : body;

        if (!LooksLikeJson(contentType, json)) return false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            Walk(doc.RootElement, path: string.Empty, rows);
            return rows.Count > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void Walk(JsonElement element, string path, List<KeyValuePair<string, string>> rows)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var any = false;
                foreach (var prop in element.EnumerateObject())
                {
                    any = true;
                    var childPath = path.Length == 0 ? prop.Name : path + "." + prop.Name;
                    Walk(prop.Value, childPath, rows);
                }
                if (!any) rows.Add(new KeyValuePair<string, string>(EmptyPath(path), "{}"));
                break;
            }
            case JsonValueKind.Array:
            {
                var i = 0;
                var any = false;
                foreach (var item in element.EnumerateArray())
                {
                    Walk(item, path + "[" + i + "]", rows);
                    i++;
                    any = true;
                }
                if (!any) rows.Add(new KeyValuePair<string, string>(EmptyPath(path), "[]"));
                break;
            }
            case JsonValueKind.String:
                rows.Add(new KeyValuePair<string, string>(EmptyPath(path), element.GetString() ?? string.Empty));
                break;
            case JsonValueKind.Null:
                rows.Add(new KeyValuePair<string, string>(EmptyPath(path), "null"));
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Number:
            default:
                rows.Add(new KeyValuePair<string, string>(EmptyPath(path), element.GetRawText()));
                break;
        }
    }

    private static string EmptyPath(string path) => path.Length == 0 ? "(root)" : path;

    private static bool LooksLikeJson(string? contentType, string body)
    {
        if (!string.IsNullOrEmpty(contentType) &&
            contentType.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];
            if (char.IsWhiteSpace(c)) continue;
            return c == '{' || c == '[';
        }
        return false;
    }
}

/// <summary>View model for the <c>_BodyDisplay</c> partial.</summary>
public sealed record BodyView(string? Body, string? ContentType);
