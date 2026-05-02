using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Dragonfire.TraceKit.Options;

/// <summary>
/// Controls which secrets are stripped from captured headers and bodies before they
/// reach <see cref="Abstractions.ITraceRepository"/>.
/// </summary>
public sealed class RedactionOptions
{
    /// <summary>Replacement token written in place of any redacted value.</summary>
    public string ReplacementToken { get; set; } = "[REDACTED]";

    /// <summary>
    /// Header names (case-insensitive) whose values are fully replaced with
    /// <see cref="ReplacementToken"/>. Defaults cover the common authentication / cookie
    /// headers — add to this set rather than reassigning to keep the defaults.
    /// </summary>
    public HashSet<string> SensitiveHeaders { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Proxy-Authorization",
        "Cookie",
        "Set-Cookie",
        "X-Api-Key",
        "Api-Key",
        "X-Auth-Token",
        "X-Access-Token",
        "X-Refresh-Token",
        "X-Csrf-Token",
        "X-Xsrf-Token",
    };

    /// <summary>
    /// JSON property names (case-insensitive) that are redacted in JSON request/response
    /// bodies. Matching is by property name only — values can be of any JSON type.
    /// </summary>
    public HashSet<string> SensitiveJsonProperties { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "passwd", "pwd",
        "secret", "clientSecret", "client_secret",
        "token", "accessToken", "access_token", "refreshToken", "refresh_token", "idToken", "id_token",
        "apiKey", "api_key", "apikey",
        "authorization",
        "ssn", "socialSecurity", "taxId",
        "creditCard", "credit_card", "cardNumber", "cvv", "cvc",
        "bankAccount", "routingNumber", "iban",
    };

    /// <summary>
    /// Query-string parameter names (case-insensitive) whose values are redacted in URLs.
    /// </summary>
    public HashSet<string> SensitiveQueryParameters { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "access_token", "refresh_token", "id_token", "token",
        "api_key", "apikey", "client_secret",
    };

    /// <summary>
    /// Compiled regex patterns applied to body text (request and response). The captured
    /// match — or the named group <c>secret</c> if present — is replaced with
    /// <see cref="ReplacementToken"/>.
    /// </summary>
    public List<Regex> BodyPatterns { get; set; } = new()
    {
        new Regex(@"Bearer\s+[A-Za-z0-9._\-]+", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new Regex(@"\b\d{4}[- ]?\d{4}[- ]?\d{4}[- ]?\d{4}\b", RegexOptions.Compiled),
    };
}
