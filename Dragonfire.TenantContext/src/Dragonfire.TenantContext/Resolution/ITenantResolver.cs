namespace Dragonfire.TenantContext.Resolution;

/// <summary>
/// Single-strategy tenant resolver. Implementations should be cheap and side-effect free;
/// return <see cref="TenantResolution.Unresolved"/> when this strategy is not applicable.
/// Multiple resolvers are composed by <see cref="CompositeTenantResolver"/> under
/// <see cref="TenantResolutionOptions"/>.
/// </summary>
public interface ITenantResolver
{
    /// <summary>Stable, short identifier ("header", "subdomain", "claim:tid", etc.).</summary>
    string Name { get; }

    ValueTask<TenantResolution> ResolveAsync(TenantResolutionContext context, CancellationToken cancellationToken);
}
