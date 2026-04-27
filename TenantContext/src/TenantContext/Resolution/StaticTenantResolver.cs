namespace TenantContext.Resolution;

/// <summary>
/// Always returns the same tenant. Useful as a fallback (last in the chain) for single-tenant
/// deployments, dev/test, or background workers that should run as a system tenant.
/// </summary>
public sealed class StaticTenantResolver : ITenantResolver
{
    private readonly TenantResolution _resolution;

    public StaticTenantResolver(TenantId tenantId, string source = "static")
    {
        if (tenantId.IsEmpty) throw new ArgumentException("Static resolver requires a non-empty tenant id.", nameof(tenantId));
        _resolution = TenantResolution.Resolved(tenantId, source);
        Name = source;
    }

    public string Name { get; }

    public ValueTask<TenantResolution> ResolveAsync(TenantResolutionContext context, CancellationToken cancellationToken)
        => ValueTask.FromResult(_resolution);
}
