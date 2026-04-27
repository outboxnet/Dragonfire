using Dragonfire.Caching.Models;

namespace Dragonfire.Caching.Interfaces;

/// <summary>
/// Pluggable cache storage backend.
/// </summary>
public interface ICacheProvider : IDisposable
{
    /// <summary>Human-readable name of the provider (used in logs and metrics).</summary>
    string Name { get; }

    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    Task SetAsync<T>(string key, T value, CacheEntryOptions options, CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove all keys matching a glob-style pattern (e.g. <c>user:*</c>).
    /// Note: distributed backends may not natively support pattern removal;
    /// callers should prefer tag-based invalidation for distributed scenarios.
    /// </summary>
    Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically get-or-set using the provided factory. Implementations should
    /// use internal locking to prevent stampedes.
    /// </summary>
    Task<T> GetOrAddAsync<T>(string key, Func<Task<T>> factory, CacheEntryOptions options, CancellationToken cancellationToken = default);

    /// <summary>Refresh the sliding expiration window of a key without retrieving its value.</summary>
    Task RefreshAsync(string key, CancellationToken cancellationToken = default);

    Task<IDictionary<string, T?>> GetMultipleAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default);

    Task SetMultipleAsync<T>(IDictionary<string, T> values, CacheEntryOptions options, CancellationToken cancellationToken = default);

    CacheStatistics GetStatistics();

    Task ClearAsync(CancellationToken cancellationToken = default);
}
