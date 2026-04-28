using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Dragonfire.Features.AspNetCore;

/// <summary>
/// Action filter that honours <see cref="FeatureGateAttribute"/> declarations on controllers
/// and action methods. Multiple gates on the same action are AND-combined: every gate must be
/// enabled for the request to proceed.
///
/// <para>Wire it up globally:</para>
/// <code>
/// builder.Services.AddDragonfireFeaturesAspNetCore();
/// builder.Services.AddControllers(o => o.Filters.AddService&lt;FeatureGateActionFilter&gt;());
/// </code>
/// </summary>
public sealed class FeatureGateActionFilter : IAsyncActionFilter
{
    private readonly IFeatureResolver _resolver;

    public FeatureGateActionFilter(IFeatureResolver resolver)
    {
        _resolver = resolver;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var gates = ResolveGates(context);
        if (gates.Length == 0)
        {
            await next().ConfigureAwait(false);
            return;
        }

        foreach (var gate in gates)
        {
            var enabled = await _resolver
                .IsEnabledAsync(gate.FeatureName, context: null, context.HttpContext.RequestAborted)
                .ConfigureAwait(false);

            if (!enabled)
            {
                context.Result = new StatusCodeResult(gate.DeniedStatusCode);
                return;
            }
        }

        await next().ConfigureAwait(false);
    }

    private static FeatureGateAttribute[] ResolveGates(ActionExecutingContext context)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor descriptor)
            return System.Array.Empty<FeatureGateAttribute>();

        var methodGates     = descriptor.MethodInfo.GetCustomAttributes(typeof(FeatureGateAttribute), inherit: true).OfType<FeatureGateAttribute>();
        var controllerGates = descriptor.ControllerTypeInfo.GetCustomAttributes(typeof(FeatureGateAttribute), inherit: true).OfType<FeatureGateAttribute>();
        return controllerGates.Concat(methodGates).ToArray();
    }
}
