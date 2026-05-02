using Dragonfire.TraceKit.Abstractions;
using Dragonfire.TraceKit.Options;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dragonfire.TraceKit.AspNetCore.Http;

/// <summary>
/// Inserts <see cref="TraceKitDelegatingHandler"/> at the head of the additional-handlers
/// chain for every <see cref="HttpClient"/> created by <see cref="IHttpClientFactory"/> —
/// named, typed, and the default — without each call site having to opt in.
/// </summary>
internal sealed class TraceKitHandlerBuilderFilter : IHttpMessageHandlerBuilderFilter
{
    private readonly ITraceContextAccessor _accessor;
    private readonly ITraceWriter _writer;
    private readonly ITraceRedactor _redactor;
    private readonly IOptions<TraceKitOptions> _options;
    private readonly ILogger<TraceKitDelegatingHandler> _logger;

    public TraceKitHandlerBuilderFilter(
        ITraceContextAccessor accessor,
        ITraceWriter writer,
        ITraceRedactor redactor,
        IOptions<TraceKitOptions> options,
        ILogger<TraceKitDelegatingHandler> logger)
    {
        _accessor = accessor;
        _writer = writer;
        _redactor = redactor;
        _options = options;
        _logger = logger;
    }

    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next)
    {
        return builder =>
        {
            // Run the rest of the pipeline first so our handler sits OUTSIDE the user's
            // additional handlers — capturing the request as the user wrote it and the
            // response after every other handler has had a chance to react.
            next(builder);

            var handler = new TraceKitDelegatingHandler(
                _accessor, _writer, _redactor, _options, _logger,
                string.IsNullOrEmpty(builder.Name) ? "Default" : builder.Name);

            builder.AdditionalHandlers.Insert(0, handler);
        };
    }
}
