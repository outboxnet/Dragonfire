using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Dragonfire.Logging.Grpc.Configuration
{
    /// <summary>
    /// Runtime options for <see cref="Interceptors.DragonfireClientLoggingInterceptor"/>.
    /// Configure once at DI registration time; the same instance is injected into every call.
    /// </summary>
    public sealed class DragonfireGrpcClientOptions
    {
        /// <summary>
        /// Extract and log scalar fields from the outgoing proto request message as
        /// <c>Request.{fieldName}</c> customDimensions.
        /// Field names use the proto JSON name (lowerCamelCase, e.g. <c>tenantId</c>).
        /// Only applies to unary and server-streaming calls — client-streaming and
        /// bidirectional calls send a stream of requests, not a single message.
        /// Default: <c>true</c>.
        /// </summary>
        public bool LogRequestFields { get; set; } = true;

        /// <summary>
        /// Extract and log scalar fields from the incoming proto response message as
        /// <c>Response.{fieldName}</c> customDimensions.
        /// Field names use the proto JSON name (lowerCamelCase).
        /// Only applies to unary and client-streaming calls — server-streaming and
        /// bidirectional calls receive a stream of responses, not a single message.
        /// Default: <c>true</c>.
        /// </summary>
        public bool LogResponseFields { get; set; } = true;

        /// <summary>
        /// Include <c>Dragonfire.StackTrace</c> in the error scope on unhandled exceptions.
        /// Default: <c>true</c>.
        /// </summary>
        public bool LogStackTrace { get; set; } = true;

        /// <summary>
        /// Proto JSON field names to suppress (case-insensitive).
        /// Matches both <c>Request.*</c> and <c>Response.*</c> dimensions.
        /// Example: add <c>"authToken"</c> to suppress <c>Request.authToken</c>.
        /// </summary>
        public ISet<string> ExcludeFields { get; set; } =
            new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Whitelist of proto JSON field names to include (case-insensitive).
        /// When non-empty, only fields in this set are logged; all others are skipped.
        /// When empty (default), all non-excluded scalar fields are logged.
        /// </summary>
        public ISet<string> IncludeFields { get; set; } =
            new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Log level used for successful calls.
        /// Default: <c>LogLevel.Information</c>.
        /// </summary>
        public LogLevel LogLevel { get; set; } = LogLevel.Information;

        internal bool ShouldLog(string fieldName)
        {
            if (ExcludeFields.Contains(fieldName)) return false;
            if (IncludeFields.Count > 0 && !IncludeFields.Contains(fieldName)) return false;
            return true;
        }
    }
}
