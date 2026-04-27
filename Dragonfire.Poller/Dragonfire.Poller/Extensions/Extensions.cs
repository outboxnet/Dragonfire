using Microsoft.Extensions.DependencyInjection;
using Dragonfire.Poller.Models;
using Dragonfire.Poller.Services;

namespace Dragonfire.Poller.Extensions
{
    /// <summary>
    /// Additional DI extension methods that complement <see cref="PollingServiceRegistration"/>.
    /// </summary>
    public static class PollingExtensions
    {
        /// <summary>
        /// Registers Azure Application Insights as the metrics backend for the polling framework.
        /// Call this after <c>AddPollingService&lt;TRequest, TResponse&gt;()</c>.
        /// Requires <c>TelemetryClient</c> to already be registered in the container
        /// (e.g. via <c>services.AddApplicationInsightsTelemetry()</c>).
        /// </summary>
        public static IServiceCollection AddApplicationInsightsPollingMetrics(
            this IServiceCollection services)
        {
            // Replace the default NoOp tracker with the App Insights implementation.
            services.AddSingleton<IPollingMetricsTracker, AppInsightsMetricsTracker>();
            return services;
        }
    }
}
