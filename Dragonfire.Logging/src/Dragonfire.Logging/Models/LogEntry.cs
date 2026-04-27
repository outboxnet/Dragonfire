using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Dragonfire.Logging.Models
{
    /// <summary>
    /// A single structured log entry produced by the HTTP filter or service interceptor.
    /// HTTP-specific fields are null for service-layer entries; service-specific fields
    /// are null for HTTP entries.
    /// </summary>
    public sealed class LogEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public LogLevel Level { get; set; } = LogLevel.Information;
        public string? CorrelationId { get; set; }
        public string? TraceId { get; set; }

        // ── HTTP context (populated by DragonfireLoggingFilter) ──────────────
        public string? HttpMethod { get; set; }
        public string? Path { get; set; }
        public string? QueryString { get; set; }
        public string? UserAgent { get; set; }
        public string? ClientIp { get; set; }
        public Dictionary<string, string>? Headers { get; set; }
        public int? StatusCode { get; set; }

        // ── Service layer (populated by DragonfireInterceptor) ────────────────
        public string? ServiceName { get; set; }
        public string? MethodName { get; set; }
        public object? MethodArguments { get; set; }
        public object? MethodResult { get; set; }

        // ── Common ────────────────────────────────────────────────────────────
        public string? UserId { get; set; }
        public Dictionary<string, object>? CustomData { get; set; }

        /// <summary>
        /// Individual structured-log properties promoted from <c>[LogProperty]</c>
        /// attributes. Keys already carry their prefix:
        /// <list type="bullet">
        ///   <item><c>Request.{Key}</c> — from action parameters and their DTO types</item>
        ///   <item><c>Response.{Key}</c> — from response DTO properties</item>
        /// </list>
        /// Each entry is emitted as its own scope key and appears directly in
        /// <c>customDimensions</c>, queryable in KQL without any JSON parsing:
        /// <code>| where customDimensions["Request.TenantId"] == "acme"</code>
        /// </summary>
        public Dictionary<string, object?>? NamedProperties { get; set; }

        public double ElapsedMilliseconds { get; set; }
        public bool IsError { get; set; }
        public string? ErrorMessage { get; set; }
        public string? StackTrace { get; set; }
        public string? CustomContext { get; set; }
    }
}
