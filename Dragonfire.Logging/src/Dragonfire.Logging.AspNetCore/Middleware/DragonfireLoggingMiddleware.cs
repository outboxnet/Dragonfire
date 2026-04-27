using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dragonfire.Logging.AspNetCore.Configuration;
using Dragonfire.Logging.Configuration;
using Dragonfire.Logging.Models;
using Dragonfire.Logging.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Dragonfire.Logging.AspNetCore.Middleware
{
    /// <summary>
    /// Lightweight request/response logging middleware for <b>minimal-API</b> projects,
    /// or any pipeline that does not use MVC controllers.
    ///
    /// For controller-based Web API / MVC projects the
    /// <see cref="Filters.DragonfireLoggingFilter"/> is preferred because it reads the
    /// typed <c>ObjectResult.Value</c> without touching the response stream.
    ///
    /// Register in <c>Program.cs</c> (after <c>UseRouting</c>):
    /// <code>
    /// app.UseDragonfireLogging();
    /// </code>
    /// </summary>
    public sealed class DragonfireLoggingMiddleware
    {
        private const string CorrelationIdHeader = "X-Correlation-ID";

        private readonly RequestDelegate _next;
        private readonly DragonfireLoggingOptions _coreOptions;
        private readonly DragonfireAspNetCoreOptions _httpOptions;

        public DragonfireLoggingMiddleware(
            RequestDelegate next,
            DragonfireLoggingOptions coreOptions,
            DragonfireAspNetCoreOptions httpOptions)
        {
            _next        = next;
            _coreOptions = coreOptions;
            _httpOptions = httpOptions;
        }

        public async Task InvokeAsync(
            HttpContext context,
            IDragonfireLoggingService loggingService,
            ILogFilterService filterService)
        {
            var path = context.Request.Path.Value ?? string.Empty;

            // Skip excluded paths.
            if (_httpOptions.ExcludePaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            {
                await _next(context);
                return;
            }

            var correlationId = EnsureCorrelationId(context);
            var ambient       = loggingService.GetOrCreateContext(correlationId);

            var entry = new LogEntry
            {
                CorrelationId = correlationId,
                TraceId       = Activity.Current?.Id ?? context.TraceIdentifier,
                HttpMethod    = context.Request.Method,
                Path          = path,
                QueryString   = context.Request.QueryString.ToString(),
                ClientIp      = context.Connection.RemoteIpAddress?.ToString(),
                UserAgent     = context.Request.Headers["User-Agent"].ToString(),
                Level         = _coreOptions.DefaultLogLevel,
                UserId        = ambient.UserId,
                CustomData    = ambient.CustomData.Count > 0 ? new Dictionary<string, object>(ambient.CustomData) : null
            };

            // ── Request body ─────────────────────────────────────────────────
            if (_httpOptions.EnableRequestLogging
                && !HttpMethods.IsGet(context.Request.Method)
                && context.Request.ContentLength > 0)
            {
                context.Request.EnableBuffering();
                var body = await new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true)
                    .ReadToEndAsync();
                context.Request.Body.Position = 0;

                //TODO: minimal API
                //if (!string.IsNullOrEmpty(body))
                  //  entry.RequestData = filterService.FilterString(body, _coreOptions.DefaultMaxContentLength);
            
            }

            // ── Execute pipeline ─────────────────────────────────────────────
            Stream? originalBody = null;
            MemoryStream? responseCapture = null;

            if (_httpOptions.EnableResponseLogging && _httpOptions.CaptureResponseBodyInMiddleware)
            {
                originalBody    = context.Response.Body;
                responseCapture = new MemoryStream();
                context.Response.Body = responseCapture;
            }

            var sw = Stopwatch.StartNew();
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                entry.IsError      = true;
                entry.Level        = LogLevel.Error;
                entry.ErrorMessage = ex.Message;
                if (_coreOptions.IncludeStackTraceOnError)
                    entry.StackTrace = ex.StackTrace;
                throw;
            }
            finally
            {
                sw.Stop();
                entry.ElapsedMilliseconds = sw.ElapsedMilliseconds;
                entry.StatusCode          = context.Response.StatusCode;

                // ── Response body (opt-in) ────────────────────────────────────
                if (responseCapture != null && originalBody != null)
                {
                    responseCapture.Position = 0;
                    var responseText = await new StreamReader(responseCapture).ReadToEndAsync();
                    responseCapture.Position = 0;
                    await responseCapture.CopyToAsync(originalBody);
                    context.Response.Body = originalBody;

                    if (!string.IsNullOrEmpty(responseText))
                       // entry.ResponseData = filterService.FilterString(
                         //   responseText, _coreOptions.DefaultMaxContentLength);

                    await responseCapture.DisposeAsync();
                }

                if (entry.StatusCode >= 500 && !entry.IsError)
                    entry.Level = LogLevel.Error;

                await loggingService.LogAsync(entry);
                loggingService.ClearContext(correlationId);
            }
        }

        private static string EnsureCorrelationId(HttpContext context)
        {
            if (context.Request.Headers.TryGetValue(CorrelationIdHeader, out var existing)
                && !string.IsNullOrWhiteSpace(existing))
                return existing.ToString();

            var id = Guid.NewGuid().ToString();
            context.Response.OnStarting(() =>
            {
                if (!context.Response.Headers.ContainsKey(CorrelationIdHeader))
                    context.Response.Headers[CorrelationIdHeader] = id;
                return Task.CompletedTask;
            });
            return id;
        }
    }
}
