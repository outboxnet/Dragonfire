using System.Diagnostics;
using System.IO;
using System.Text;
using Dragonfire.TraceKit.Abstractions;
using Dragonfire.TraceKit.AspNetCore.Internal;
using Dragonfire.TraceKit.Context;
using Dragonfire.TraceKit.Models;
using Dragonfire.TraceKit.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dragonfire.TraceKit.AspNetCore.Middleware;

/// <summary>
/// Captures the inbound HTTP exchange (request + response), establishes the per-request
/// <see cref="ITraceContext"/> so outbound HttpClient calls inherit the correlation id,
/// and hands the resulting <see cref="ApiTrace"/> to the bounded-channel writer.
/// </summary>
/// <remarks>
/// Body capture is a copy-as-it-streams design: the request body is buffered with
/// <c>EnableBuffering</c> so the application pipeline reads it normally, and the response
/// body is wrapped in a tee stream so the client receives bytes with no extra latency.
/// All TraceKit work runs inside try/catch — a failure in tracing is logged at warning
/// level and the request continues unaffected.
/// </remarks>
public sealed class TraceKitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly TraceKitOptions _options;
    private readonly ITraceWriter _writer;
    private readonly ITraceRedactor _redactor;
    private readonly TraceContextAccessor _accessor;
    private readonly ILogger<TraceKitMiddleware> _logger;

    public TraceKitMiddleware(
        RequestDelegate next,
        IOptions<TraceKitOptions> options,
        ITraceWriter writer,
        ITraceRedactor redactor,
        TraceContextAccessor accessor,
        ILogger<TraceKitMiddleware> logger)
    {
        _next = next;
        _options = options.Value;
        _writer = writer;
        _redactor = redactor;
        _accessor = accessor;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.Enabled || ShouldIgnore(context.Request.Path))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var correlationId = ResolveCorrelationId(context);
        var traceContext = new TraceContext(correlationId, ResolveTenantId(context), ResolveUserId(context));

        // Push correlation id back into Activity tags so downstream telemetry agrees.
        Activity.Current?.AddTag("dragonfire.tracekit.correlation_id", correlationId);

        using var _ = _accessor.BeginScope(traceContext);

        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        // Capture request body (best-effort).
        string? requestBody = null;
        try
        {
            requestBody = await TryReadRequestBodyAsync(context).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "TraceKit: failed to capture inbound request body.");
        }

        // Wrap response body so we can tee it.
        var originalBody = context.Response.Body;
        Stream? teeStream = null;
        MemoryStream? captureStream = null;
        if (_options.CaptureInboundBodies)
        {
            captureStream = new MemoryStream();
            teeStream = new TeeStream(originalBody, captureStream);
            context.Response.Body = teeStream;
        }

        Exception? captured = null;
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            captured = ex;
            throw;
        }
        finally
        {
            stopwatch.Stop();

            // Ensure any buffered bytes are flushed to the original stream before we restore it.
            if (teeStream is not null)
            {
                try { await teeStream.FlushAsync().ConfigureAwait(false); } catch { /* ignore */ }
                context.Response.Body = originalBody;
            }

            try
            {
                var trace = BuildTrace(context, traceContext, startedAt, stopwatch, requestBody, captureStream, captured);
                _writer.TryEnqueue(trace);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TraceKit: failed to build inbound trace; trace dropped.");
            }
            finally
            {
                captureStream?.Dispose();
            }
        }
    }

    private bool ShouldIgnore(PathString path)
    {
        if (!path.HasValue) return false;
        var value = path.Value!;
        foreach (var prefix in _options.IgnoredPathPrefixes)
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-Correlation-Id", out var hv) && !string.IsNullOrWhiteSpace(hv))
            return hv.ToString();
        if (!string.IsNullOrEmpty(context.TraceIdentifier))
            return context.TraceIdentifier;
        return Guid.NewGuid().ToString("N");
    }

    private static string? ResolveTenantId(HttpContext context)
    {
        if (context.Items.TryGetValue("TenantId", out var v) && v is string s) return s;
        return context.User?.FindFirst("tenant_id")?.Value
            ?? context.User?.FindFirst("tid")?.Value;
    }

    private static string? ResolveUserId(HttpContext context)
        => context.User?.FindFirst("sub")?.Value
            ?? context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    private async Task<string?> TryReadRequestBodyAsync(HttpContext context)
    {
        if (!_options.CaptureInboundBodies) return null;
        if (!BodyCapture.ShouldCapture(context.Request.ContentType, _options)) return null;

        context.Request.EnableBuffering();
        var encoding = ResolveEncoding(context.Request.ContentType);
        var body = await BodyCapture.ReadStreamAsync(
            context.Request.Body, encoding, _options.MaxBodyBytes, context.RequestAborted).ConfigureAwait(false);
        if (context.Request.Body.CanSeek)
            context.Request.Body.Position = 0;
        return body;
    }

    private ApiTrace BuildTrace(
        HttpContext context,
        ITraceContext traceContext,
        DateTimeOffset startedAt,
        Stopwatch stopwatch,
        string? requestBody,
        MemoryStream? responseCapture,
        Exception? exception)
    {
        var trace = new ApiTrace
        {
            CorrelationId = traceContext.CorrelationId,
            Sequence = 0,
            Kind = TraceKind.Inbound,
            Method = context.Request.Method,
            Url = _redactor.RedactUrl(BuildFullUrl(context.Request)),
            OperationName = ResolveRouteTemplate(context),
            StatusCode = exception is null ? context.Response.StatusCode : null,
            StartedAtUtc = startedAt,
            CompletedAtUtc = startedAt + stopwatch.Elapsed,
            RequestContentType = context.Request.ContentType,
            ResponseContentType = context.Response.ContentType,
            TenantId = traceContext.TenantId,
            UserId = traceContext.UserId,
            ExceptionType = exception?.GetType().FullName,
            ExceptionMessage = exception?.Message,
        };

        foreach (var h in context.Request.Headers)
            trace.RequestHeaders[h.Key] = _redactor.RedactHeader(h.Key, h.Value.ToString());
        foreach (var h in context.Response.Headers)
            trace.ResponseHeaders[h.Key] = _redactor.RedactHeader(h.Key, h.Value.ToString());

        if (_options.CaptureInboundBodies)
        {
            trace.RequestBody = _redactor.RedactBody(requestBody, context.Request.ContentType);

            if (responseCapture is not null && BodyCapture.ShouldCapture(context.Response.ContentType, _options))
            {
                var bytes = responseCapture.ToArray();
                var encoding = ResolveEncoding(context.Response.ContentType);
                var raw = BodyCapture.ReadBytes(bytes, encoding, _options.MaxBodyBytes);
                trace.ResponseBody = _redactor.RedactBody(raw, context.Response.ContentType);
            }
        }

        return trace;
    }

    private static string BuildFullUrl(HttpRequest request)
        => $"{request.Scheme}://{request.Host}{request.PathBase}{request.Path}{request.QueryString}";

    private static string? ResolveRouteTemplate(HttpContext context)
    {
        var endpoint = context.Features.Get<IEndpointFeature>()?.Endpoint;
        return endpoint?.DisplayName;
    }

    private static Encoding ResolveEncoding(string? contentType)
    {
        if (!string.IsNullOrEmpty(contentType))
        {
            try
            {
                var parsed = new System.Net.Mime.ContentType(contentType);
                if (!string.IsNullOrEmpty(parsed.CharSet))
                    return Encoding.GetEncoding(parsed.CharSet);
            }
            catch
            {
                // fall through
            }
        }
        return Encoding.UTF8;
    }
}
