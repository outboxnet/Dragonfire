using Dragonfire.Caching.Interfaces;

namespace Dragonfire.Caching.Models;

/// <summary>
/// Configures how a cache entry is stored. At most one expiration type may be set.
/// </summary>
public sealed class CacheEntryOptions
{
    /// <summary>Absolute expiration at a specific point in time.</summary>
    public DateTimeOffset? AbsoluteExpiration { get; set; }

    /// <summary>Absolute expiration relative to the time of storage.</summary>
    public TimeSpan? AbsoluteExpirationRelativeToNow { get; set; }

    /// <summary>Sliding expiration — resets on each cache hit.</summary>
    public TimeSpan? SlidingExpiration { get; set; }

    /// <summary>Eviction priority (memory provider only).</summary>
    public CacheItemPriority Priority { get; set; } = CacheItemPriority.Normal;

    /// <summary>Logical size of the entry (memory provider only, used with SizeLimit).</summary>
    public long? Size { get; set; }

    /// <summary>Tags used for group invalidation via <see cref="Interfaces.ITagIndex"/>.</summary>
    public HashSet<string> Tags { get; set; } = [];

    /// <summary>Optional custom policy that overrides the expiration fields above.</summary>
    public ICachePolicy? CustomPolicy { get; set; }

    /// <summary>5-minute sliding expiration at Normal priority.</summary>
    public static CacheEntryOptions Default => new() { SlidingExpiration = TimeSpan.FromMinutes(5) };

    /// <summary>Absolute expiration relative to now.</summary>
    public static CacheEntryOptions Absolute(TimeSpan expiration) =>
        new() { AbsoluteExpirationRelativeToNow = expiration };

    /// <summary>Sliding expiration.</summary>
    public static CacheEntryOptions Sliding(TimeSpan expiration) =>
        new() { SlidingExpiration = expiration };

    /// <summary>Entry is never evicted by the cache engine.</summary>
    public static CacheEntryOptions NeverExpire =>
        new() { Priority = CacheItemPriority.NeverRemove };

    /// <summary>Throws if the options are in an invalid state.</summary>
    public void Validate()
    {
        if (AbsoluteExpiration.HasValue && AbsoluteExpirationRelativeToNow.HasValue)
            throw new InvalidOperationException(
                $"Cannot set both {nameof(AbsoluteExpiration)} and {nameof(AbsoluteExpirationRelativeToNow)}.");

        if (AbsoluteExpirationRelativeToNow.HasValue && AbsoluteExpirationRelativeToNow.Value <= TimeSpan.Zero)
            throw new ArgumentException("Must be positive.", nameof(AbsoluteExpirationRelativeToNow));

        if (SlidingExpiration.HasValue && SlidingExpiration.Value <= TimeSpan.Zero)
            throw new ArgumentException("Must be positive.", nameof(SlidingExpiration));
    }
}
