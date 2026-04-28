namespace Dragonfire.Features;

/// <summary>
/// Builds a <see cref="FeatureContext"/> from the ambient request — typically reads
/// <c>Dragonfire.TenantContext.ITenantContextAccessor</c> for tenant id and the auth principal
/// for user id. The AspNetCore integration ships an <c>HttpContext</c>-aware implementation;
/// background-task pipelines plug their own.
/// </summary>
public interface IFeatureContextAccessor
{
    FeatureContext Current { get; }
}
