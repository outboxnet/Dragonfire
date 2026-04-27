using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Dragonfire.Logging.AspNetCore.Configuration;
using Dragonfire.Logging.Attributes;
using Dragonfire.Logging.Configuration;
using Dragonfire.Logging.Models;
using Dragonfire.Logging.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Dragonfire.Logging.AspNetCore.Filters
{
    /// <summary>
    /// Global MVC action filter. Promotes <c>[LogProperty]</c>-annotated action
    /// parameters and response DTO properties directly to <c>customDimensions</c>
    /// as <c>Request.*</c> and <c>Response.*</c> keys — no JSON serialisation,
    /// no payload blobs, direct field access only.
    /// </summary>
    public sealed class DragonfireLoggingFilter : IAsyncActionFilter
    {
        private const string CorrelationIdHeader = "X-Correlation-ID";

        /// <summary>
        /// Key used in <c>HttpContext.Items</c> to publish named properties for downstream
        /// telemetry enrichers (e.g. DragonfireTelemetryInitializer in the AI package).
        /// Value: <c>Dictionary&lt;string, object?&gt;</c>
        /// </summary>
        public static readonly object NamedPropertiesItemKey = new();

        private readonly IDragonfireLoggingService   _loggingService;
        private readonly DragonfireLoggingOptions    _coreOptions;
        private readonly DragonfireAspNetCoreOptions _httpOptions;

        public DragonfireLoggingFilter(
            IDragonfireLoggingService loggingService,
            DragonfireLoggingOptions coreOptions,
            DragonfireAspNetCoreOptions httpOptions)
        {
            _loggingService = loggingService;
            _coreOptions    = coreOptions;
            _httpOptions    = httpOptions;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var path = context.HttpContext.Request.Path.Value ?? string.Empty;

            if (_httpOptions.ExcludePaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            {
                await next();
                return;
            }

            var attr          = ResolveLogAttribute(context);
            var correlationId = EnsureCorrelationId(context.HttpContext);

            var entry = new LogEntry
            {
                CorrelationId = correlationId,
                TraceId       = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier,
                HttpMethod    = context.HttpContext.Request.Method,
                Path          = path,
                QueryString   = context.HttpContext.Request.QueryString.ToString(),
                ClientIp      = context.HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent     = context.HttpContext.Request.Headers["User-Agent"].ToString(),
                Level         = attr?.Level ?? _coreOptions.DefaultLogLevel,
                CustomContext = attr?.CustomContext,
            };

            // Merge ambient per-request context (UserId, custom key/values).
            var rctx = _loggingService.GetOrCreateContext(correlationId);
            entry.UserId     = rctx.UserId;
            entry.CustomData = rctx.CustomData.Count > 0
                ? new Dictionary<string, object>(rctx.CustomData)
                : null;

            // Promote [LogProperty]-annotated action parameters → Request.* customDimensions
            if (_httpOptions.EnableRequestLogging && attr?.LogRequest != false)
                PromoteRequestProperties(entry, context);

            // ── Execute the action ────────────────────────────────────────────
            var sw       = Stopwatch.StartNew();
            var executed = await next();
            sw.Stop();

            entry.ElapsedMilliseconds = sw.Elapsed.TotalMilliseconds;
            entry.StatusCode          = executed.HttpContext.Response.StatusCode;

            if (executed.Exception is { } ex)
            {
                entry.IsError      = true;
                entry.Level        = LogLevel.Error;
                entry.ErrorMessage = ex.Message;
                if (_coreOptions.IncludeStackTraceOnError)
                    entry.StackTrace = ex.StackTrace;
            }
            else if (_httpOptions.EnableResponseLogging && attr?.LogResponse != false)
            {
                // Promote [LogProperty]-annotated response DTO properties → Response.* customDimensions
                var responseValue = executed.Result switch
                {
                    ObjectResult { Value: not null } obj  => obj.Value,
                    JsonResult   { Value: not null } json => json.Value,
                    _                                     => null
                };

                if (responseValue is not null)
                    PromoteResponseProperties(entry, responseValue);
            }

            if (_httpOptions.LogValidationErrors && attr?.LogValidationErrors != false
                && !context.ModelState.IsValid)
            {
                entry.CustomData ??= new Dictionary<string, object>();
                entry.CustomData["ValidationErrors"] = context.ModelState
                    .Where(kv => kv.Value?.Errors.Count > 0)
                    .ToDictionary(
                        kv => kv.Key,
                        kv => (object)kv.Value!.Errors.Select(e => e.ErrorMessage).ToArray());
            }

            // Make named properties available to telemetry enrichers via HttpContext.Items
            if (entry.NamedProperties is { Count: > 0 })
                executed.HttpContext.Items[DragonfireLoggingFilter.NamedPropertiesItemKey] = entry.NamedProperties;

            await _loggingService.LogAsync(entry);
            _loggingService.ClearContext(correlationId);
        }

        // ── [LogProperty] promotion ──────────────────────────────────────────

        /// <summary>
        /// Reads MVC's already-bound action arguments. For each parameter decorated
        /// with <c>[LogProperty]</c>, and for each <c>[LogProperty]</c>-decorated
        /// property on a bound object, writes a <c>Request.{Key}</c> entry into
        /// <see cref="LogEntry.NamedProperties"/> — direct field access, no JSON.
        /// </summary>
        private static void PromoteRequestProperties(LogEntry entry, ActionExecutingContext context)
        {
            var named = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            if (context.ActionDescriptor is ControllerActionDescriptor cad)
            {
                foreach (var param in cad.MethodInfo.GetParameters())
                {
                    if (!context.ActionArguments.TryGetValue(param.Name ?? string.Empty, out var argValue))
                        continue;

                    // Parameter itself is decorated with [LogProperty]
                    var paramAttr = param.GetCustomAttribute<LogPropertyAttribute>();
                    if (paramAttr is not null)
                    {
                        var key = paramAttr.Name ?? param.Name ?? $"param{param.Position}";
                        named[$"Request.{key}"] = argValue;
                    }

                    // Walk bound object's properties for [LogProperty]
                    if (argValue is not null)
                        PromoteDtoProperties(named, argValue, prefix: "Request");
                }
            }

            if (named.Count > 0)
                MergeNamed(entry, named);
        }

        /// <summary>
        /// Walks the response value's public properties for <c>[LogProperty]</c>
        /// and writes a <c>Response.{Key}</c> entry — direct property read, no serialisation.
        /// </summary>
        private static void PromoteResponseProperties(LogEntry entry, object responseValue)
        {
            var named = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            PromoteDtoProperties(named, responseValue, prefix: "Response");

            if (named.Count > 0)
                MergeNamed(entry, named);
        }

        /// <summary>
        /// Scans <paramref name="obj"/>'s public instance properties for
        /// <c>[LogProperty]</c> and adds <c>{prefix}.{key}</c> entries.
        /// </summary>
        private static void PromoteDtoProperties(
            Dictionary<string, object?> named, object obj, string prefix)
        {
            foreach (var prop in obj.GetType().GetProperties(
                BindingFlags.Public | BindingFlags.Instance))
            {
                var lpAttr = prop.GetCustomAttribute<LogPropertyAttribute>();
                if (lpAttr is null) continue;

                var key = lpAttr.Name ?? prop.Name;
                try { named[$"{prefix}.{key}"] = prop.GetValue(obj); }
                catch { /* skip unreadable properties */ }
            }
        }

        // ── Misc helpers ──────────────────────────────────────────────────────

        private static LogAttribute? ResolveLogAttribute(ActionExecutingContext context)
            => context.ActionDescriptor.EndpointMetadata.OfType<LogAttribute>().FirstOrDefault()
               ?? context.Controller?.GetType()
                   .GetCustomAttributes(typeof(LogAttribute), inherit: true)
                   .FirstOrDefault() as LogAttribute;

        private static string EnsureCorrelationId(HttpContext http)
        {
            if (http.Request.Headers.TryGetValue(CorrelationIdHeader, out var existing)
                && !string.IsNullOrWhiteSpace(existing))
                return existing.ToString();

            var id = Guid.NewGuid().ToString();
            http.Response.Headers[CorrelationIdHeader] = id;
            return id;
        }

        private static void MergeNamed(LogEntry entry, Dictionary<string, object?> source)
        {
            entry.NamedProperties ??= new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in source)
                entry.NamedProperties.TryAdd(k, v);
        }
    }
}
