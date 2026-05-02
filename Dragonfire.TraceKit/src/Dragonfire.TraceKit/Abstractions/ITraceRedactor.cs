namespace Dragonfire.TraceKit.Abstractions;

/// <summary>
/// Strips secrets from header values, URLs, and bodies before they are recorded.
/// The default implementation honours <see cref="Options.RedactionOptions"/>; replace
/// with a custom registration to plug in domain-specific rules.
/// </summary>
public interface ITraceRedactor
{
    /// <summary>Returns the header value with full redaction if the header name is sensitive, otherwise unchanged.</summary>
    string RedactHeader(string name, string value);

    /// <summary>Redacts query-string values whose parameter names match the configured set.</summary>
    string RedactUrl(string url);

    /// <summary>Redacts JSON-property values, regex matches, and any custom rules from a body string.</summary>
    string? RedactBody(string? body, string? contentType);
}
