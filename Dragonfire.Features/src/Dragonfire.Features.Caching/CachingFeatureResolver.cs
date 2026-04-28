using System;
using System.Threading;
using System.Threading.Tasks;
using Dragonfire.Caching.Interfaces;

namespace Dragonfire.Features.Caching;

/// <summary>
/// Decorator that caches resolution decisions per <c>(feature, tenant, user)</c> tuple. The
/// decorator is registered with a short TTL so the next refresh tick of the source can
/// invalidate stale verdicts naturally; tag-based invalidation lets a single tenant's
/// decisions be flushed when its custom rules change.
///
/// <para>Cache key shape: <c>features:{feature}:{tenantOrNone}:{userOrNone}</c></para>
/// <para>Tags applied: <c>features:{feature}</c>, <c>features-tenant:{tenantId}</c></para>
/// </summary>
public sealed class CachingFeatureResolver : IFeatureResolver
{
    private readonly IFeatureResolver _inner;
    private readonly ICacheService _cache;
    private readonly IFeatureContextAccessor _contextAccessor;
    private readonly CachingFeatureResolverOptions _options;

    public CachingFeatureResolver(
        IFeatureResolver inner,
        ICacheService cache,
        IFeatureContextAccessor contextAccessor,
        CachingFeatureResolverOptions options)
    {
        _inner           = inner;
        _cache           = cache;
        _contextAccessor = contextAccessor;
        _options         = options;
    }

    public Task<bool> IsEnabledAsync(
        string featureName,
        FeatureContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var ctx = context ?? _contextAccessor.Current;
        var key = BuildKey(featureName, ctx);

        return _cache.GetOrAddWithTagsAsync(
            key,
            () => _inner.IsEnabledAsync(featureName, ctx, cancellationToken),
            tags: BuildTags(featureName, ctx),
            configureOptions: opts => opts.AbsoluteExpirationRelativeToNow = _options.Ttl,
            cancellationToken: cancellationToken);
    }

    private static string BuildKey(string featureName, FeatureContext ctx)
        => $"features:{featureName}:{ctx.TenantId ?? "none"}:{ctx.UserId ?? "none"}";

    private static string[] BuildTags(string featureName, FeatureContext ctx)
    {
        if (string.IsNullOrEmpty(ctx.TenantId))
            return new[] { $"features:{featureName}" };

        return new[]
        {
            $"features:{featureName}",
            $"features-tenant:{ctx.TenantId}",
        };
    }
}
