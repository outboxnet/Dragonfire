using Microsoft.Extensions.Options;

namespace Dragonfire.TenantContext.Resolution;

/// <summary>
/// Default <see cref="ITenantResolutionPipeline"/>: runs the registered resolvers in order
/// and applies the configured ambiguity / missing policies.
/// </summary>
public sealed class CompositeTenantResolver : ITenantResolutionPipeline
{
    private readonly IReadOnlyList<ITenantResolver> _resolvers;
    private readonly TenantResolutionOptions _options;

    public CompositeTenantResolver(IEnumerable<ITenantResolver> resolvers, IOptions<TenantResolutionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(resolvers);
        ArgumentNullException.ThrowIfNull(options);
        _resolvers = resolvers as IReadOnlyList<ITenantResolver> ?? resolvers.ToArray();
        _options = options.Value ?? new TenantResolutionOptions();
        _options.Validate();
    }

    public async ValueTask<TenantInfo> ResolveAsync(TenantResolutionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        TenantResolution first = TenantResolution.Unresolved;
        List<(string Source, string TenantId)>? candidates = null;

        foreach (var resolver in _resolvers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TenantResolution result;
            try
            {
                result = await resolver.ResolveAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not TenantResolutionException)
            {
                throw new TenantResolutionException($"Resolver '{resolver.Name}' threw an exception.", ex);
            }

            if (!result.IsResolved) continue;

            if (!first.IsResolved)
            {
                first = result;
                if (_options.ShortCircuitOnFirstMatch && _options.OnAmbiguous != AmbiguityPolicy.Throw)
                    break;

                candidates ??= new List<(string, string)>();
                candidates.Add((result.Source, result.TenantId.Value));
                continue;
            }

            // Already have a candidate — check ambiguity.
            var same = _options.TenantIdComparer.Equals(first.TenantId.Value, result.TenantId.Value);
            if (!same)
            {
                if (_options.OnAmbiguous == AmbiguityPolicy.Throw)
                {
                    candidates ??= new List<(string, string)>();
                    candidates.Add((result.Source, result.TenantId.Value));
                    throw new TenantResolutionException(
                        $"Ambiguous tenant resolution: resolver '{first.Source}' returned '{first.TenantId.Value}' but '{result.Source}' returned '{result.TenantId.Value}'.")
                    {
                        Candidates = candidates,
                    };
                }
                // UseFirst — keep going only if not short-circuiting; otherwise we'd already have broken out.
            }

            if (_options.ShortCircuitOnFirstMatch) break;
        }

        if (first.IsResolved)
            return new TenantInfo(first.TenantId, first.Source, first.Properties);

        return _options.OnMissing switch
        {
            MissingTenantPolicy.AllowEmpty => TenantInfo.None,
            MissingTenantPolicy.UseDefault => new TenantInfo(_options.DefaultTenant, source: "default"),
            MissingTenantPolicy.Throw => throw new TenantResolutionException("No resolver produced a tenant id."),
            _ => TenantInfo.None,
        };
    }
}
