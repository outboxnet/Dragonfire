using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.DependencyInjection;

namespace Dragonfire.Logging.ApplicationInsights.Extensions
{
    public static class DragonfireApplicationInsightsExtensions
    {
        /// <summary>
        /// Registers <see cref="DragonfireTelemetryInitializer"/> so that all
        /// [LogProperty]-promoted fields (Request.*, Response.*) are automatically
        /// copied to every Application Insights telemetry item fired during the request.
        /// <para>
        /// Call this AFTER <c>services.AddApplicationInsightsTelemetry()</c> and
        /// AFTER <c>services.AddDragonfireAspNetCore(...)</c>.
        /// </para>
        /// </summary>
        public static IServiceCollection AddDragonfireApplicationInsights(
            this IServiceCollection services)
        {
            services.AddHttpContextAccessor();
            services.AddSingleton<ITelemetryInitializer, DragonfireTelemetryInitializer>();
            return services;
        }
    }
}
