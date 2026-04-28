using System.Threading;
using System.Threading.Tasks;

namespace Dragonfire.Features;

/// <summary>
/// Evaluates whether a feature is enabled for a given <see cref="FeatureContext"/>. The default
/// implementation is purely in-memory; the <c>Dragonfire.Features.Caching</c> package wraps it
/// to cache decisions per (feature, tenant, user) tuple.
/// </summary>
public interface IFeatureResolver
{
    /// <summary>
    /// Resolve the feature flag against the given context. When <paramref name="context"/> is
    /// <c>null</c>, the resolver pulls the ambient context from <see cref="IFeatureContextAccessor"/>.
    /// </summary>
    Task<bool> IsEnabledAsync(
        string featureName,
        FeatureContext? context = null,
        CancellationToken cancellationToken = default);
}
