using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Dragonfire.Features.AspNetCore;

/// <summary>
/// Builds a <see cref="FeatureContext"/> from the current <see cref="HttpContext"/>:
/// tenant id from a configurable header (default <c>X-Tenant-Id</c>) and user id from the
/// configurable claim type (default <see cref="ClaimTypes.NameIdentifier"/>).
///
/// <para>If you already use <c>Dragonfire.TenantContext</c>, register your own accessor that
/// reads <c>ITenantContextAccessor.Current</c> instead — Dragonfire.Features deliberately
/// avoids a hard dependency on TenantContext.</para>
/// </summary>
public sealed class HttpContextFeatureContextAccessor : IFeatureContextAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly FeatureAspNetCoreOptions _options;

    public HttpContextFeatureContextAccessor(
        IHttpContextAccessor httpContextAccessor,
        IOptions<FeatureAspNetCoreOptions> options)
    {
        _httpContextAccessor = httpContextAccessor;
        _options             = options.Value;
    }

    public FeatureContext Current
    {
        get
        {
            var http = _httpContextAccessor.HttpContext;
            if (http is null) return FeatureContext.Empty;

            string? tenantId = null;
            if (http.Request.Headers.TryGetValue(_options.TenantHeaderName, out var hv) && hv.Count > 0)
                tenantId = hv[0];

            // Tenant claim wins over header — claims survive header-stripping proxies.
            if (http.User?.Identity?.IsAuthenticated == true)
            {
                var tenantClaim = http.User.FindFirst(_options.TenantClaimType);
                if (tenantClaim is not null) tenantId = tenantClaim.Value;
            }

            string? userId = null;
            var userClaim = http.User?.FindFirst(_options.UserClaimType);
            if (userClaim is not null) userId = userClaim.Value;

            return new FeatureContext(tenantId, userId);
        }
    }
}
