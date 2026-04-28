using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Dragonfire.Features.Internal;

/// <summary>
/// Default in-memory <see cref="IFeatureResolver"/>. Looks up the definition in
/// <see cref="IFeatureStore"/>, evaluates rules in order, and falls back to
/// <see cref="FeatureDefinition.DefaultEnabled"/>. Unknown features return <c>false</c>.
/// </summary>
public sealed class DefaultFeatureResolver : IFeatureResolver
{
    private readonly IFeatureStore _store;
    private readonly IFeatureContextAccessor _contextAccessor;
    private readonly ILogger<DefaultFeatureResolver> _logger;

    public DefaultFeatureResolver(
        IFeatureStore store,
        IFeatureContextAccessor contextAccessor,
        ILogger<DefaultFeatureResolver> logger)
    {
        _store           = store;
        _contextAccessor = contextAccessor;
        _logger          = logger;
    }

    public Task<bool> IsEnabledAsync(
        string featureName,
        FeatureContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var def = _store.Get(featureName);
        if (def is null)
        {
            _logger.LogDebug("Feature '{Feature}' is not registered — denying.", featureName);
            return Task.FromResult(false);
        }

        var ctx = context ?? _contextAccessor.Current;

        foreach (var rule in def.Rules)
        {
            var verdict = rule.Evaluate(ctx);
            if (verdict.HasValue)
                return Task.FromResult(verdict.Value);
        }

        return Task.FromResult(def.DefaultEnabled);
    }
}
