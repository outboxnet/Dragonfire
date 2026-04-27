using System;
using Dragonfire.Logging.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dragonfire.Logging.Configuration
{
    /// <summary>
    /// Core configuration for Dragonfire.Logging.
    /// Controls service-layer interception and shared concerns such as
    /// redaction policy, log level, and payload truncation.
    ///
    /// HTTP request/response options live in
    /// <c>Dragonfire.Logging.AspNetCore.Configuration.DragonfireAspNetCoreOptions</c>.
    /// </summary>
    public sealed class DragonfireLoggingOptions
    {
        // ── Service interception ──────────────────────────────────────────────

        /// <summary>
        /// When <c>true</c>, every service registered before
        /// <c>AddDragonfireLogging</c> whose implementation implements
        /// <see cref="Abstractions.ILoggable"/> is automatically wrapped with a
        /// Castle DynamicProxy logging interceptor via Scrutor's <c>Decorate</c>.
        /// Default: <c>false</c>.
        /// </summary>
        public bool EnableServiceInterception { get; set; } = false;

        // ── Shared ────────────────────────────────────────────────────────────

        /// <summary>
        /// Maximum characters serialised for any single payload (0 = unlimited).
        /// Can be overridden per method via <see cref="Attributes.LogAttribute.MaxContentLength"/>.
        /// Default: <c>10 000</c>.
        /// </summary>
        public int DefaultMaxContentLength { get; set; } = 10_000;

        /// <summary>
        /// Default maximum nesting depth for serialised payloads (request, response,
        /// method arguments, return values). Can be overridden per method via
        /// <see cref="Attributes.LogAttribute.MaxDepth"/>.
        /// <list type="bullet">
        ///   <item><c>0</c> — unlimited (full serialisation).</item>
        ///   <item><c>1</c> (default) — top-level scalars only; nested objects/arrays become
        ///   <c>[N fields]</c> / <c>[N items]</c> to keep logs concise.</item>
        ///   <item><c>N</c> — N levels of nesting.</item>
        /// </list>
        /// </summary>
        public int DefaultMaxDepth { get; set; } = 1;

        /// <summary>Include the full stack trace in error log entries. Default: <c>true</c>.</summary>
        public bool IncludeStackTraceOnError { get; set; } = true;

        /// <summary>Severity used when no <see cref="Attributes.LogAttribute"/> is present. Default: <c>Information</c>.</summary>
        public LogLevel DefaultLogLevel { get; set; } = LogLevel.Information;

        /// <summary>Sensitive-data redaction policy applied to all logged payloads.</summary>
        public SensitiveDataPolicy SensitiveDataPolicy { get; set; } = new();

        /// <summary>
        /// Optional callback invoked for every <see cref="LogEntry"/> after the
        /// standard <see cref="ILogger"/> write. Forward entries to a custom sink
        /// (database, message bus, OpenTelemetry, etc.) here.
        /// </summary>
        public Action<LogEntry>? CustomLogAction { get; set; }

        /// <summary>DI lifetime used for <c>IDragonfireLoggingService</c>. Default: <c>Scoped</c>.</summary>
        public ServiceLifetime LoggingServiceLifetime { get; set; } = ServiceLifetime.Scoped;
    }
}
