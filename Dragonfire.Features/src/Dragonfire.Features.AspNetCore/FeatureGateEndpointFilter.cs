using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Dragonfire.Features.AspNetCore;

/// <summary>
/// Endpoint filter for minimal-API routes. Use the <c>RequireFeature</c> extension instead of
/// constructing this directly:
/// <code>
/// app.MapPost("/orders", CreateOrder).RequireFeature("NewCheckout");
/// </code>
/// </summary>
public sealed class FeatureGateEndpointFilter : IEndpointFilter
{
    private readonly IFeatureResolver _resolver;
    private readonly string _featureName;
    private readonly int _deniedStatusCode;

    public FeatureGateEndpointFilter(IFeatureResolver resolver, string featureName, int deniedStatusCode)
    {
        _resolver         = resolver;
        _featureName      = featureName;
        _deniedStatusCode = deniedStatusCode;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var enabled = await _resolver
            .IsEnabledAsync(_featureName, context: null, context.HttpContext.RequestAborted)
            .ConfigureAwait(false);

        if (!enabled)
            return Results.StatusCode(_deniedStatusCode);

        return await next(context).ConfigureAwait(false);
    }
}
