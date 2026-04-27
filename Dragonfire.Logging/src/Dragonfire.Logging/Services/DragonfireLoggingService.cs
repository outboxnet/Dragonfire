using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dragonfire.Logging.Configuration;
using Dragonfire.Logging.Logging;
using Dragonfire.Logging.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Dragonfire.Logging.Services
{
    /// <inheritdoc cref="IDragonfireLoggingService"/>
    public sealed class DragonfireLoggingService : IDragonfireLoggingService
    {
        private readonly ILogger<DragonfireLoggingService> _logger;
        private readonly DragonfireLoggingOptions _options;

        private readonly ConcurrentDictionary<string, LoggingContext> _contexts = new();

        public DragonfireLoggingService(
            ILogger<DragonfireLoggingService> logger,
            DragonfireLoggingOptions options)
        {
            _logger  = logger;
            _options = options;
        }

        /// <inheritdoc/>
        public void Log(LogEntry entry)
        {
            var source = GetSource(entry);
            var scope  = BuildScope(entry, source);

            using (_logger.BeginScope(scope))
            {
                if (entry.IsError)
                    _logger.Log(entry.Level,
                        "[Dragonfire] {Source} FAILED in {ElapsedMs}ms — {ErrorMessage}",
                        source,
                        entry.ElapsedMilliseconds,
                        entry.ErrorMessage);
                else
                    _logger.Log(entry.Level,
                        "[Dragonfire] {Source} completed in {ElapsedMs}ms",
                        source,
                        entry.ElapsedMilliseconds);
            }

            _options.CustomLogAction?.Invoke(entry);
        }

        /// <inheritdoc/>
        public Task LogAsync(LogEntry entry)
        {
            Log(entry);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task LogCustomAsync(
            string correlationId,
            string message,
            Dictionary<string, object>? data = null,
            LogLevel level = LogLevel.Information)
        {
            var ctx = GetOrCreateContext(correlationId);

            return LogAsync(new LogEntry
            {
                CorrelationId = correlationId,
                Level         = level,
                CustomContext = message,
                UserId        = ctx.UserId,
                CustomData    = ctx.CustomData.Count > 0
                    ? new Dictionary<string, object>(ctx.CustomData)
                    : null
            });
        }

        /// <inheritdoc/>
        public LoggingContext GetOrCreateContext(string correlationId)
            => _contexts.GetOrAdd(correlationId, id => new LoggingContext { CorrelationId = id });

        /// <inheritdoc/>
        public void ClearContext(string correlationId)
            => _contexts.TryRemove(correlationId, out _);

        // ── Structured scope builder ─────────────────────────────────────────

        private static DragonfireScopeState BuildScope(LogEntry entry, string source)
        {
            var data  = new Dictionary<string, object>();
            var label = entry.IsError
                ? $"[Dragonfire] {source} FAILED"
                : $"[Dragonfire] {source}";
            var scope = new DragonfireScopeState(data, label);

            // ── Identity ─────────────────────────────────────────────────────
            AddIfSet(data, "Dragonfire.CorrelationId", entry.CorrelationId);
            AddIfSet(data, "Dragonfire.TraceId",       entry.TraceId);
            AddIfSet(data, "Dragonfire.UserId",        entry.UserId);
            AddIfSet(data, "Dragonfire.CustomContext", entry.CustomContext);

            // ── HTTP context ──────────────────────────────────────────────────
            AddIfSet(data, "Dragonfire.HttpMethod",  entry.HttpMethod);
            AddIfSet(data, "Dragonfire.Path",        entry.Path);
            AddIfSet(data, "Dragonfire.QueryString", entry.QueryString);
            AddIfSet(data, "Dragonfire.ClientIp",    entry.ClientIp);
            AddIfSet(data, "Dragonfire.UserAgent",   entry.UserAgent);

            if (entry.StatusCode.HasValue)
                data["Dragonfire.StatusCode"] = entry.StatusCode.Value;

            // ── Service layer ─────────────────────────────────────────────────
            AddIfSet(data, "Dragonfire.ServiceName", entry.ServiceName);
            AddIfSet(data, "Dragonfire.MethodName",  entry.MethodName);

            // ── Performance ───────────────────────────────────────────────────
            data["Dragonfire.ElapsedMs"] = entry.ElapsedMilliseconds;

            // ── Error ─────────────────────────────────────────────────────────
            if (entry.IsError)
            {
                data["Dragonfire.IsError"]      = true;
                AddIfSet(data, "Dragonfire.ErrorMessage", entry.ErrorMessage);
                AddIfSet(data, "Dragonfire.StackTrace",   entry.StackTrace);
            }

            // ── Service payload blobs (optional, when set by service interceptor) ──
            AddJson(data, "Dragonfire.MethodArguments", entry.MethodArguments);
            AddJson(data, "Dragonfire.MethodResult",    entry.MethodResult);

            // ── Custom data — flattened to individual Dragonfire.Custom.* keys ──
            if (entry.CustomData is { Count: > 0 })
            {
                foreach (var (key, value) in entry.CustomData)
                {
                    if (value is not null)
                        data[$"Dragonfire.Custom.{key}"] = value;
                }
            }

            // ── [LogProperty]-promoted fields — already carry Request.*/Response.* prefix ──
            // Applied by DragonfireLoggingFilter (HTTP) or DragonfireProxy (service runtime).
            // Each key is emitted verbatim as its own customDimension.
            if (entry.NamedProperties is { Count: > 0 })
            {
                foreach (var (key, value) in entry.NamedProperties)
                {
                    if (value is not null && !string.IsNullOrWhiteSpace(key))
                        data[key] = value;
                }
            }

            return scope;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string GetSource(LogEntry entry)
        {
            if (!string.IsNullOrEmpty(entry.ServiceName))
            {
                return string.IsNullOrEmpty(entry.MethodName)
                    ? entry.ServiceName
                    : $"{entry.ServiceName}.{entry.MethodName}";
            }

            return string.IsNullOrEmpty(entry.HttpMethod)
                ? (entry.Path ?? "unknown")
                : $"{entry.HttpMethod} {entry.Path}";
        }

        private static void AddIfSet(Dictionary<string, object> data, string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                data[key] = value;
        }

        private static void AddJson(Dictionary<string, object> data, string key, object? value)
        {
            if (value is null) return;

            string json;
            if (value is string s)
                json = s;
            else
            {
                try   { json = JsonConvert.SerializeObject(value, Formatting.None); }
                catch { json = value.ToString() ?? string.Empty; }
            }

            if (!string.IsNullOrWhiteSpace(json))
                data[key] = json;
        }
    }
}
