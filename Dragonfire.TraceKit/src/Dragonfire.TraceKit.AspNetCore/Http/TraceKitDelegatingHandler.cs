using System.Diagnostics;
using System.Net.Http;
using System.Text;
using Dragonfire.TraceKit.Abstractions;
using Dragonfire.TraceKit.AspNetCore.Internal;
using Dragonfire.TraceKit.Models;
using Dragonfire.TraceKit.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dragonfire.TraceKit.AspNetCore.Http;

/// <summary>
/// Records every outbound HttpClient call performed inside the scope of an inbound
/// request — request, response, status, duration, exception. Sequence numbers come from
/// <see cref="ITraceContext.NextOutboundSequence"/> so the order is correct even when
/// multiple third-party calls run in parallel via <c>Task.WhenAll</c>.
/// </summary>
/// <remarks>
/// When no inbound request is in flight (background work, console host, etc.) the handler
/// is transparent: it just forwards the call. Capturing never alters the outgoing request
/// or the consumed response — both bodies are read into memory only when the content
/// type is whitelisted, and the original <see cref="HttpContent"/> is left intact for
/// the calling code.
/// </remarks>
public sealed class TraceKitDelegatingHandler : DelegatingHandler
{
    private readonly ITraceContextAccessor _accessor;
    private readonly ITraceWriter _writer;
    private readonly ITraceRedactor _redactor;
    private readonly TraceKitOptions _options;
    private readonly ILogger<TraceKitDelegatingHandler> _logger;
    private readonly string _httpClientName;

    public TraceKitDelegatingHandler(
        ITraceContextAccessor accessor,
        ITraceWriter writer,
        ITraceRedactor redactor,
        IOptions<TraceKitOptions> options,
        ILogger<TraceKitDelegatingHandler> logger,
        string httpClientName)
    {
        _accessor = accessor;
        _writer = writer;
        _redactor = redactor;
        _options = options.Value;
        _logger = logger;
        _httpClientName = httpClientName;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var ctx = _accessor.Current;
        if (!_options.Enabled || ctx is null)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var sequence = ctx.NextOutboundSequence();
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        string? requestBody = null;
        if (_options.CaptureOutboundBodies && request.Content is not null)
        {
            try
            {
                requestBody = await ReadContentAsync(request.Content, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "TraceKit: failed to capture outbound request body for {HttpClientName}.", _httpClientName);
            }
        }

        HttpResponseMessage? response = null;
        Exception? captured = null;
        try
        {
            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return response;
        }
        catch (Exception ex)
        {
            captured = ex;
            throw;
        }
        finally
        {
            stopwatch.Stop();

            string? responseBody = null;
            if (_options.CaptureOutboundBodies && response?.Content is not null)
            {
                try
                {
                    responseBody = await ReadContentAsync(response.Content, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "TraceKit: failed to capture outbound response body for {HttpClientName}.", _httpClientName);
                }
            }

            try
            {
                var trace = BuildTrace(request, response, ctx, sequence, startedAt, stopwatch, requestBody, responseBody, captured);
                _writer.TryEnqueue(trace);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TraceKit: failed to build outbound trace for {HttpClientName}; trace dropped.", _httpClientName);
            }
        }
    }

    private async Task<string?> ReadContentAsync(HttpContent content, CancellationToken cancellationToken)
    {
        var contentType = content.Headers.ContentType?.MediaType;
        if (!BodyCapture.ShouldCapture(contentType, _options)) return null;

        // LoadIntoBufferAsync ensures the original consumer can still read the content
        // afterwards — HttpContent buffers internally so a second read is fine.
        await content.LoadIntoBufferAsync().ConfigureAwait(false);
        var bytes = await content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        var encoding = ResolveEncoding(content);
        return BodyCapture.ReadBytes(bytes, encoding, _options.MaxBodyBytes);
    }

    private static Encoding ResolveEncoding(HttpContent content)
    {
        var charset = content.Headers.ContentType?.CharSet;
        if (!string.IsNullOrEmpty(charset))
        {
            try { return Encoding.GetEncoding(charset.Trim('"')); } catch { /* fall through */ }
        }
        return Encoding.UTF8;
    }

    private ApiTrace BuildTrace(
        HttpRequestMessage request,
        HttpResponseMessage? response,
        ITraceContext ctx,
        int sequence,
        DateTimeOffset startedAt,
        Stopwatch stopwatch,
        string? requestBody,
        string? responseBody,
        Exception? exception)
    {
        var trace = new ApiTrace
        {
            CorrelationId = ctx.CorrelationId,
            Sequence = sequence,
            Kind = TraceKind.OutboundThirdParty,
            Method = request.Method.Method,
            Url = _redactor.RedactUrl(request.RequestUri?.ToString() ?? string.Empty),
            OperationName = _httpClientName,
            StatusCode = response is null ? null : (int)response.StatusCode,
            StartedAtUtc = startedAt,
            CompletedAtUtc = startedAt + stopwatch.Elapsed,
            RequestContentType = request.Content?.Headers.ContentType?.ToString(),
            ResponseContentType = response?.Content?.Headers.ContentType?.ToString(),
            TenantId = ctx.TenantId,
            UserId = ctx.UserId,
            ExceptionType = exception?.GetType().FullName,
            ExceptionMessage = exception?.Message,
            RequestBody = _redactor.RedactBody(requestBody, request.Content?.Headers.ContentType?.MediaType),
            ResponseBody = _redactor.RedactBody(responseBody, response?.Content?.Headers.ContentType?.MediaType),
        };

        foreach (var h in request.Headers)
            trace.RequestHeaders[h.Key] = _redactor.RedactHeader(h.Key, string.Join(",", h.Value));
        if (request.Content is not null)
        {
            foreach (var h in request.Content.Headers)
                trace.RequestHeaders[h.Key] = _redactor.RedactHeader(h.Key, string.Join(",", h.Value));
        }

        if (response is not null)
        {
            foreach (var h in response.Headers)
                trace.ResponseHeaders[h.Key] = _redactor.RedactHeader(h.Key, string.Join(",", h.Value));
            if (response.Content is not null)
            {
                foreach (var h in response.Content.Headers)
                    trace.ResponseHeaders[h.Key] = _redactor.RedactHeader(h.Key, string.Join(",", h.Value));
            }
        }

        return trace;
    }
}
