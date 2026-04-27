using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Dragonfire.TenantContext.Resolution;

namespace Dragonfire.TenantContext.AspNetCore;

/// <summary>
/// Resolves the tenant for the current HTTP request and pushes it onto the
/// <see cref="ITenantContextSetter"/> scope for the duration of the pipeline.
/// </summary>
public sealed class TenantContextMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ITenantResolutionPipeline _pipeline;
    private readonly ITenantContextSetter _setter;
    private readonly TenantContextHttpOptions _http;
    private readonly ILogger<TenantContextMiddleware> _logger;

    public TenantContextMiddleware(
        RequestDelegate next,
        ITenantResolutionPipeline pipeline,
        ITenantContextSetter setter,
        IOptions<TenantContextHttpOptions> httpOptions,
        ILogger<TenantContextMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _setter = setter ?? throw new ArgumentNullException(nameof(setter));
        _http = httpOptions?.Value ?? new TenantContextHttpOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var ctx = new TenantResolutionContext { CancellationToken = httpContext.RequestAborted }
            .With(TenantResolutionContext.HttpContextKey, httpContext)
            .With(TenantResolutionContext.PrincipalKey, httpContext.User);

        TenantInfo tenant;
        try
        {
            tenant = await _pipeline.ResolveAsync(ctx, httpContext.RequestAborted).ConfigureAwait(false);
        }
        catch (TenantResolutionException ex)
        {
            _logger.LogWarning(ex, "Tenant resolution failed for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
            if (_http.WriteFailureResponse && !httpContext.Response.HasStarted)
            {
                httpContext.Response.StatusCode = _http.FailureStatusCode;
                await httpContext.Response.WriteAsync(ex.Message, httpContext.RequestAborted).ConfigureAwait(false);
                return;
            }
            throw;
        }

        if (!tenant.IsResolved)
        {
            await _next(httpContext).ConfigureAwait(false);
            return;
        }

        using var scope = _setter.BeginScope(tenant);

        if (!string.IsNullOrEmpty(_http.ResponseHeader) && !httpContext.Response.HasStarted)
        {
            httpContext.Response.OnStarting(state =>
            {
                var (response, headerName, headerValue) = ((HttpResponse, string, string))state;
                if (!response.Headers.ContainsKey(headerName))
                    response.Headers[headerName] = headerValue;
                return Task.CompletedTask;
            }, (httpContext.Response, _http.ResponseHeader, tenant.TenantId.Value));
        }

        await _next(httpContext).ConfigureAwait(false);
    }
}
