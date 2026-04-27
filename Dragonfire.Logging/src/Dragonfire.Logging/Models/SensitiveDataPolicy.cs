using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Dragonfire.Logging.Models
{
    /// <summary>
    /// Defines which fields and patterns should be redacted before a value is logged.
    /// Sensitive field names are matched case-insensitively against JSON property names.
    /// Regex patterns are applied to serialised string values.
    /// </summary>
    public sealed class SensitiveDataPolicy
    {
        /// <summary>
        /// Field names (case-insensitive) whose values are replaced with <c>[REDACTED]</c>.
        /// </summary>
        public static readonly HashSet<string> DefaultSensitiveFields = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "password", "passwd", "pwd", "secret",
            "token", "accesstoken", "refreshtoken",
            "apikey", "api_key", "authorization", "cookie",
            "creditcard", "credit_card", "cardnumber", "cvv",
            "ssn", "socialsecurity", "taxid",
            "bankaccount", "routingnumber"
        };

        /// <summary>Field names to redact. Defaults to <see cref="DefaultSensitiveFields"/>.</summary>
        public HashSet<string> SensitiveFields { get; set; } = new(DefaultSensitiveFields, System.StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Compiled regex patterns applied to every serialised string value.
        /// Key = pattern, Value = replacement token.
        /// </summary>
        public Dictionary<Regex, string> RedactionPatterns { get; set; } = new()
        {
            [new Regex(@"\b\d{4}[- ]?\d{4}[- ]?\d{4}[- ]?\d{4}\b",   RegexOptions.Compiled)] = "[CREDIT_CARD_REDACTED]",
            [new Regex(@"\b\d{3}[- ]?\d{2}[- ]?\d{4}\b",              RegexOptions.Compiled)] = "[SSN_REDACTED]",
            [new Regex(@"Bearer\s+[\w-]+\.[\w-]+\.[\w-]+",             RegexOptions.Compiled)] = "Bearer [JWT_REDACTED]",
        };

        public bool RedactEmails { get; set; } = true;
        public bool RedactPhoneNumbers { get; set; } = true;
    }
}
