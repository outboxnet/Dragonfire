using System.Security.Claims;

namespace Dragonfire.Features.AspNetCore;

/// <summary>
/// Knobs for the AspNetCore integration: which header / claim to read for tenant and user ids.
/// </summary>
public sealed class FeatureAspNetCoreOptions
{
    public string TenantHeaderName { get; set; } = "X-Tenant-Id";
    public string TenantClaimType { get; set; } = "tenant_id";
    public string UserClaimType { get; set; } = ClaimTypes.NameIdentifier;
}
