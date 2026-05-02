using System.Text;
using System.Text.Json;
using Dragonfire.TraceKit.Abstractions;
using Dragonfire.TraceKit.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dragonfire.TraceKit.Redaction;

/// <summary>
/// Default redactor honouring <see cref="RedactionOptions"/>:
/// <list type="bullet">
///   <item>Sensitive header names → value replaced with the configured token.</item>
///   <item>Sensitive query-string parameters → values replaced.</item>
///   <item>JSON bodies → matching property values rewritten as the token.</item>
///   <item>Non-JSON bodies → regex patterns applied.</item>
/// </list>
/// All operations are best-effort; a malformed JSON body falls back to regex on the raw
/// string so a bad payload can never crash the request.
/// </summary>
public sealed class DefaultTraceRedactor : ITraceRedactor
{
    private readonly TraceKitOptions _options;
    private readonly ILogger<DefaultTraceRedactor> _logger;

    public DefaultTraceRedactor(IOptions<TraceKitOptions> options, ILogger<DefaultTraceRedactor> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public string RedactHeader(string name, string value)
        => _options.Redaction.SensitiveHeaders.Contains(name)
            ? _options.Redaction.ReplacementToken
            : value;

    /// <inheritdoc />
    public string RedactUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return url;
        var queryIndex = url.IndexOf('?');
        if (queryIndex < 0) return url;

        var path = url.Substring(0, queryIndex);
        var query = url.Substring(queryIndex + 1);
        var pairs = query.Split('&');
        var sensitive = _options.Redaction.SensitiveQueryParameters;
        var token = _options.Redaction.ReplacementToken;
        var changed = false;

        for (var i = 0; i < pairs.Length; i++)
        {
            var pair = pairs[i];
            var eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            var key = pair.Substring(0, eq);
            if (!sensitive.Contains(key)) continue;
            pairs[i] = key + "=" + token;
            changed = true;
        }

        return changed ? path + "?" + string.Join('&', pairs) : url;
    }

    /// <inheritdoc />
    public string? RedactBody(string? body, string? contentType)
    {
        if (string.IsNullOrEmpty(body)) return body;

        if (LooksLikeJson(contentType, body))
        {
            try
            {
                return RedactJson(body!);
            }
            catch (JsonException ex)
            {
                _logger.LogDebug(ex, "TraceKit: failed to parse body as JSON for redaction; falling back to pattern redaction.");
                // fall through to regex
            }
        }

        return ApplyPatterns(body!);
    }

    private static bool LooksLikeJson(string? contentType, string body)
    {
        if (!string.IsNullOrEmpty(contentType) &&
            contentType.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        // Heuristic for missing content-type
        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];
            if (char.IsWhiteSpace(c)) continue;
            return c == '{' || c == '[';
        }
        return false;
    }

    private string RedactJson(string body)
    {
        using var doc = JsonDocument.Parse(body);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteRedacted(doc.RootElement, writer);
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private void WriteRedacted(JsonElement element, Utf8JsonWriter writer)
    {
        var sensitive = _options.Redaction.SensitiveJsonProperties;
        var token = _options.Redaction.ReplacementToken;

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var prop in element.EnumerateObject())
                {
                    writer.WritePropertyName(prop.Name);
                    if (sensitive.Contains(prop.Name))
                        writer.WriteStringValue(token);
                    else
                        WriteRedacted(prop.Value, writer);
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteRedacted(item, writer);
                writer.WriteEndArray();
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }

    private string ApplyPatterns(string body)
    {
        var token = _options.Redaction.ReplacementToken;
        var result = body;
        foreach (var pattern in _options.Redaction.BodyPatterns)
        {
            result = pattern.Replace(result, token);
        }
        return result;
    }
}
