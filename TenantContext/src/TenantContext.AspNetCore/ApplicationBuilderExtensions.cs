using Microsoft.AspNetCore.Builder;

namespace TenantContext.AspNetCore;

/// <summary>Pipeline registration for the tenant middleware.</summary>
public static class TenantContextApplicationBuilderExtensions
{
    /// <summary>
    /// Adds <see cref="TenantContextMiddleware"/> to the request pipeline. Place it AFTER
    /// <c>UseRouting</c> if you use <c>RouteValueTenantResolver</c>, and AFTER <c>UseAuthentication</c>
    /// if you use <c>ClaimTenantResolver</c>; otherwise place it as early as possible.
    /// </summary>
    public static IApplicationBuilder UseTenantContext(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<TenantContextMiddleware>();
    }
}
