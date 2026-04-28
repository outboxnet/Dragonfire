namespace Dragonfire.Features.Internal;

/// <summary>
/// Fallback accessor for callers that have no AspNetCore <c>HttpContext</c> in scope —
/// background services, console apps, tests. Always returns <see cref="FeatureContext.Empty"/>;
/// percentage and allow-list rules will simply abstain.
/// </summary>
public sealed class DefaultFeatureContextAccessor : IFeatureContextAccessor
{
    public FeatureContext Current => FeatureContext.Empty;
}
