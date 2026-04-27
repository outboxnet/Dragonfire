using System;
using Dragonfire.Logging.AspNetCore.Configuration;
using Dragonfire.Logging.AspNetCore.Filters;
using Dragonfire.Logging.AspNetCore.Middleware;
using Dragonfire.Logging.Configuration;
using Dragonfire.Logging.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dragonfire.Logging.AspNetCore.Extensions
{
    public static class DragonfireAspNetCoreExtensions
    {
        /// <summary>
        /// Registers the complete Dragonfire.Logging stack for an ASP.NET Core application:
        ///
        /// <list type="bullet">
        ///   <item>All core services (<see cref="DragonfireLoggingExtensions.AddDragonfireLogging"/>).</item>
        ///   <item><see cref="DragonfireLoggingFilter"/> — global MVC action filter that logs
        ///     every controller action request and response. Works for both Web API and MVC.</item>
        ///   <item><see cref="DragonfireLoggingMiddleware"/> registered in DI, ready to be added
        ///     to the pipeline via <see cref="UseDragonfireLogging"/>.</item>
        /// </list>
        ///
        /// <b>Call this after all your service registrations</b> so the ILoggable scan
        /// (when enabled) can find every decorated service.
        ///
        /// Typical usage in <c>Program.cs</c>:
        /// <code>
        /// // 1. Register application services first.
        /// builder.Services.AddScoped&lt;IOrderService, OrderService&gt;(); // OrderService : ILoggable
        ///
        /// // 2. Add Dragonfire — core options + HTTP options.
        /// builder.Services.AddDragonfireAspNetCore(
        ///     core: opt =>
        ///     {
        ///         opt.EnableServiceInterception = true;
        ///         opt.CustomLogAction = entry => db.Insert(entry);
        ///     },
        ///     http: opt =>
        ///     {
        ///         opt.ExcludePaths = new[] { "/health", "/ready" };
        ///     });
        ///
        /// var app = builder.Build();
        ///
        /// // 3. (Minimal-API only) activate the middleware.
        /// app.UseDragonfireLogging();
        /// </code>
        /// </summary>
        public static IServiceCollection AddDragonfireAspNetCore(
            this IServiceCollection services,
            Action<DragonfireLoggingOptions>? core = null,
            Action<DragonfireAspNetCoreOptions>? http = null)
        {
            // Register core services (idempotent — safe to call multiple times).
            services.AddDragonfireLogging(core);

            var httpOptions = new DragonfireAspNetCoreOptions();
            http?.Invoke(httpOptions);
            services.AddSingleton(httpOptions);

            // Filter: used for controller-based (MVC / Web API) endpoints.
            services.TryAddScoped<DragonfireLoggingFilter>();
            services.Configure<MvcOptions>(mvc =>
            {
                if (httpOptions.EnableRequestLogging || httpOptions.EnableResponseLogging)
                    mvc.Filters.AddService<DragonfireLoggingFilter>(order: int.MinValue);
            });

            return services;
        }

        /// <summary>
        /// Adds <see cref="DragonfireLoggingMiddleware"/> to the request pipeline.
        ///
        /// Use this for <b>minimal-API</b> projects or any pipeline that does not go
        /// through MVC controllers. Place it after <c>UseRouting()</c> so the route
        /// is resolved before logging begins.
        ///
        /// For controller-based projects the global MVC filter registered by
        /// <see cref="AddDragonfireAspNetCore"/> already handles logging — calling this
        /// middleware is harmless but redundant.
        /// </summary>
        public static IApplicationBuilder UseDragonfireLogging(this IApplicationBuilder app)
            => app.UseMiddleware<DragonfireLoggingMiddleware>();
    }
}
