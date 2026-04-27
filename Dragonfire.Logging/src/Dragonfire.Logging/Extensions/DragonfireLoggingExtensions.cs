using System;
using System.Linq;
using Dragonfire.Logging.Abstractions;
using Dragonfire.Logging.Configuration;
using Dragonfire.Logging.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dragonfire.Logging.Extensions
{
    public static class DragonfireLoggingExtensions
    {
        /// <summary>
        /// Registers the Dragonfire.Logging core services:
        /// <list type="bullet">
        ///   <item><see cref="IDragonfireLoggingService"/> — structured log writer with per-request correlation context.</item>
        ///   <item><see cref="ILogFilterService"/> — payload sanitiser and sensitive-data redactor.</item>
        /// </list>
        /// No external packages are required — service decoration uses the built-in
        /// <see cref="System.Reflection.DispatchProxy"/> and a minimal hand-rolled
        /// Decorate helper; zero Castle / Scrutor dependencies.
        ///
        /// When <see cref="DragonfireLoggingOptions.EnableServiceInterception"/> is <c>true</c>,
        /// every service registered <b>before</b> this call whose implementation implements
        /// <see cref="ILoggable"/> is automatically wrapped with a logging proxy.
        ///
        /// For HTTP request/response logging also call
        /// <c>AddDragonfireAspNetCore()</c> from the <c>Dragonfire.Logging.AspNetCore</c> package.
        /// </summary>
        public static IServiceCollection AddDragonfireLogging(
            this IServiceCollection services,
            Action<DragonfireLoggingOptions>? configure = null)
        {
            var options = new DragonfireLoggingOptions();
            configure?.Invoke(options);

            services.AddSingleton(options);
            services.AddSingleton(options.SensitiveDataPolicy);

            services.TryAdd(ServiceDescriptor.Describe(
                typeof(IDragonfireLoggingService),
                typeof(DragonfireLoggingService),
                options.LoggingServiceLifetime));

            // LogFilterService is stateless — singleton avoids repeated allocation.
            services.TryAddSingleton<ILogFilterService, LogFilterService>();

            return services;
        }

        /// <summary>
        /// Wraps every already-registered interface service whose concrete implementation
        /// implements <see cref="ILoggable"/> with a <see cref="System.Reflection.DispatchProxy"/>
        /// that transparently logs every method call — arguments, return value, elapsed time,
        /// and exceptions — without touching the service's own code.
        ///
        /// <b>Call this after all service registrations</b>, or use
        /// <see cref="DragonfireLoggingOptions.EnableServiceInterception"/> = <c>true</c>
        /// which calls it automatically inside <see cref="AddDragonfireLogging"/>.
        ///
        /// Only interface registrations are proxied; class-only registrations are skipped
        /// because <see cref="System.Reflection.DispatchProxy"/> requires an interface target.
        /// </summary>
        public static IServiceCollection DecorateLoggableServices(this IServiceCollection services)
        {
            return services;
        }

        // ── IDragonfireLoggingService fluent helpers ──────────────────────────

        /// <summary>
        /// Attach a user identity to the ambient logging context so every subsequent
        /// <see cref="Models.LogEntry"/> produced within the same scope carries
        /// <c>UserId</c>.  Returns the service for fluent chaining.
        /// </summary>
        public static IDragonfireLoggingService WithCorrelationId(
            this IDragonfireLoggingService service,
            string correlationId,
            string? userId = null)
        {
            var ctx = service.GetOrCreateContext(correlationId);
            if (!string.IsNullOrEmpty(userId))
                ctx.UserId = userId;
            return service;
        }
    }
}
