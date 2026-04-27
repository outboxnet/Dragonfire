namespace Dragonfire.Caching.Interfaces;

/// <summary>
/// Custom expiration policy. Assign to <see cref="Models.CacheEntryOptions.CustomPolicy"/>
/// to override TTL computation at the provider level.
/// </summary>
public interface ICachePolicy
{
    DateTimeOffset? GetExpiration();
    bool ShouldRefresh(DateTimeOffset lastAccess, DateTimeOffset now);
}

/// <summary>Convenience base class with a default refresh heuristic (20 % of remaining lifetime).</summary>
public abstract class BaseCachePolicy : ICachePolicy
{
    public abstract DateTimeOffset? GetExpiration();

    public virtual bool ShouldRefresh(DateTimeOffset lastAccess, DateTimeOffset now)
    {
        var expiration = GetExpiration();
        if (!expiration.HasValue) return false;

        var timeToExpire = expiration.Value - now;
        var totalLifetime = expiration.Value - lastAccess;
        return timeToExpire.TotalMilliseconds < totalLifetime.TotalMilliseconds * 0.2;
    }
}
