using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Dragonfire.Features;

/// <summary>
/// Loads the full set of feature definitions from a backing store. Sources are polled
/// periodically by <see cref="Dragonfire.Features.Refresh.FeatureRefreshHostedService"/>.
/// Implementations are expected to be cheap to call repeatedly — caching or change-tokens
/// belong inside the implementation.
/// </summary>
public interface IFeatureSource
{
    /// <summary>Stable identifier (e.g. <c>configuration</c>, <c>efcore</c>) — recorded in audit entries.</summary>
    string Name { get; }

    /// <summary>Snapshot every feature this source knows about.</summary>
    Task<IReadOnlyList<FeatureDefinition>> LoadAllAsync(CancellationToken cancellationToken = default);
}
