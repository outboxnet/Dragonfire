using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Acme.BillingClient;

public interface IBillingHttpLogger
{
    Task BeforeRequestAsync(HttpRequestMessage request, string operationName, CancellationToken cancellationToken);
    Task AfterResponseAsync(HttpResponseMessage response, string operationName, TimeSpan elapsed, CancellationToken cancellationToken);
    Task OnErrorAsync(Exception exception, string operationName, CancellationToken cancellationToken);
}

public sealed class DefaultBillingHttpLogger : IBillingHttpLogger
{
    private readonly ILogger<DefaultBillingHttpLogger> _logger;
    private readonly BillingLoggingOptions _options;

    public DefaultBillingHttpLogger(
        ILogger<DefaultBillingHttpLogger> logger,
        IOptions<BillingLoggingOptions> options)
    {
        _logger  = logger;
        _options = options.Value;
    }

    public Task BeforeRequestAsync(HttpRequestMessage request, string operationName, CancellationToken ct)
    {
        var (level, _) = Resolve(operationName);
        if (level is null) return Task.CompletedTask;

        _logger.Log(level.Value, "-> {Operation} {Method} {Uri}",
            operationName, request.Method, request.RequestUri);
        return Task.CompletedTask;
    }

    public Task AfterResponseAsync(HttpResponseMessage response, string operationName, TimeSpan elapsed, CancellationToken ct)
    {
        var (level, _) = Resolve(operationName);
        if (level is null) return Task.CompletedTask;

        _logger.Log(level.Value, "<- {Operation} {Status} in {Elapsed}ms",
            operationName, (int)response.StatusCode, elapsed.TotalMilliseconds);
        return Task.CompletedTask;
    }

    public Task OnErrorAsync(Exception exception, string operationName, CancellationToken ct)
    {
        _logger.LogError(exception, "x {Operation} failed", operationName);
        return Task.CompletedTask;
    }

    private (LogLevel? level, bool logBody) Resolve(string operationName)
    {
        if (_options.Endpoints.TryGetValue(operationName, out var ep))
        {
            if (ep.Disabled) return (null, false);
            return (ep.Level ?? _options.DefaultLevel, ep.LogRequestBody ?? _options.LogRequestBody);
        }
        return (_options.DefaultLevel, _options.LogRequestBody);
    }
}
