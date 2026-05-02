using Dragonfire.TraceKit.AspNetCore.Middleware;
using Microsoft.AspNetCore.Builder;

namespace Dragonfire.TraceKit.AspNetCore.Extensions;

/// <summary>Pipeline registration for <see cref="TraceKitMiddleware"/>.</summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the TraceKit middleware to the request pipeline. Place it as early as
    /// possible — typically right after diagnostics / forwarded-headers and before
    /// authentication — so the captured trace covers the full request.
    /// </summary>
    public static IApplicationBuilder UseTraceKit(this IApplicationBuilder app)
        => app.UseMiddleware<TraceKitMiddleware>();
}
