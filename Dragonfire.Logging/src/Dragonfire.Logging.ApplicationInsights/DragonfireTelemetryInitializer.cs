using Dragonfire.Logging.AspNetCore.Filters;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Http;

namespace Dragonfire.Logging.ApplicationInsights
{
    /// <summary>
    /// Enriches every Application Insights telemetry item fired during an HTTP request
    /// with the [LogProperty]-promoted fields that Dragonfire extracted from the MVC action.
    /// <para>
    /// Works for ALL telemetry types — RequestTelemetry, DependencyTelemetry,
    /// ExceptionTelemetry, EventTelemetry, MetricTelemetry, TraceTelemetry — not just logs.
    /// ILogger.BeginScope already handles TraceTelemetry; this initializer covers the rest.
    /// </para>
    /// Properties are written as-is with the Request.* / Response.* prefix so KQL queries
    /// stay consistent across traces and requests:
    /// <code>
    /// requests | where customDimensions["Request.TenantId"] == "acme"
    /// dependencies | where customDimensions["Request.TenantId"] == "acme"
    /// </code>
    /// </summary>
    public sealed class DragonfireTelemetryInitializer : ITelemetryInitializer
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public DragonfireTelemetryInitializer(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public void Initialize(ITelemetry telemetry)
        {
            var ctx = _httpContextAccessor.HttpContext;
            if (ctx is null) return;

            if (ctx.Items[DragonfireLoggingFilter.NamedPropertiesItemKey]
                    is not Dictionary<string, object?> named) return;

            // ISupportProperties is implemented by RequestTelemetry, DependencyTelemetry,
            // ExceptionTelemetry, EventTelemetry, TraceTelemetry, MetricTelemetry, PageViewTelemetry
            if (telemetry is not ISupportProperties withProps) return;

            foreach (var (key, value) in named)
            {
                if (value is null) continue;
                // Don't overwrite properties already set by the SDK or other initializers
                if (!withProps.Properties.ContainsKey(key))
                    withProps.Properties[key] = value.ToString();
            }
        }
    }
}
