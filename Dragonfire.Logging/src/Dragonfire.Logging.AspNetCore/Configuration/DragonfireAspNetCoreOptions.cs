namespace Dragonfire.Logging.AspNetCore.Configuration
{
    /// <summary>
    /// HTTP-layer configuration for Dragonfire.Logging.AspNetCore.
    /// Controls what is captured from each HTTP request and response.
    ///
    /// Service-layer options (interception, redaction policy, log level, etc.) are
    /// on <see cref="Dragonfire.Logging.Configuration.DragonfireLoggingOptions"/>.
    /// </summary>
    public sealed class DragonfireAspNetCoreOptions
    {
        /// <summary>Log inbound HTTP requests (arguments, body, route data). Default: <c>true</c>.</summary>
        public bool EnableRequestLogging { get; set; } = true;

        /// <summary>Log outbound HTTP responses (status code, result value). Default: <c>true</c>.</summary>
        public bool EnableResponseLogging { get; set; } = true;

        /// <summary>Log ASP.NET Core model-validation failures. Default: <c>true</c>.</summary>
        public bool LogValidationErrors { get; set; } = true;

        /// <summary>
        /// Path prefixes silently skipped by the filter and middleware.
        /// Extend or replace this array to match your infrastructure paths.
        /// </summary>
        public string[] ExcludePaths { get; set; } =
        {
            "/health",
            "/healthz",
            "/ready",
            "/metrics",
            "/swagger",
            "/favicon.ico"
        };

        /// <summary>
        /// When <c>true</c> the middleware captures the raw response body by swapping
        /// the response stream.  Only useful for minimal-API projects where no
        /// <c>ObjectResult</c> is available; leave <c>false</c> (default) for standard
        /// Web API / MVC controller projects — the filter reads <c>ObjectResult.Value</c>
        /// directly without touching the stream.
        /// </summary>
        public bool CaptureResponseBodyInMiddleware { get; set; } = false;
    }
}
