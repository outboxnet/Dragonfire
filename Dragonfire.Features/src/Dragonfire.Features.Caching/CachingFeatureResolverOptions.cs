using System;

namespace Dragonfire.Features.Caching;

public sealed class CachingFeatureResolverOptions
{
    /// <summary>
    /// Absolute expiration applied to every cached decision. Defaults to 30 seconds — long
    /// enough to be a hit-cache, short enough that a missed tag invalidation eventually heals.
    /// </summary>
    public TimeSpan Ttl { get; set; } = TimeSpan.FromSeconds(30);
}
