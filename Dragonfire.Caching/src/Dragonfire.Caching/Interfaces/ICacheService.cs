using Dragonfire.Caching.Models;

namespace Dragonfire.Caching.Interfaces;

/// <summary>
/// High-level cache façade. Inject this into application services.
/// </summary>
public interface ICacheService
{
    string ProviderName { get; }

    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    Task SetAsync<T>(string key, T value, Action<CacheEntryOptions>? configureOptions = null, CancellationToken cancellationToken = default);

    Task<T> GetOrAddAsync<T>(string key, Func<Task<T>> factory, Action<CacheEntryOptions>? configureOptions = null, CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Remove all keys matching a glob-style pattern (e.g. <c>user:*</c>).</summary>
    Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default);

    /// <summary>
    /// Associate <paramref name="key"/> with the given <paramref name="tags"/>, cache the value,
    /// and return it. Call <see cref="InvalidateByTagAsync"/> to evict all keys sharing a tag.
    /// </summary>
    Task<T> GetOrAddWithTagsAsync<T>(
        string key,
        Func<Task<T>> factory,
        IEnumerable<string> tags,
        Action<CacheEntryOptions>? configureOptions = null,
        CancellationToken cancellationToken = default);

    /// <summary>Evict all cache entries that were stored with the given tag.</summary>
    Task InvalidateByTagAsync(string tag, CancellationToken cancellationToken = default);

    /// <summary>
    /// Acquire an in-process lock before calling the factory, preventing cache stampedes.
    /// For distributed scenarios use a Redis-backed lock provider.
    /// </summary>
    Task<T> GetOrAddWithLockAsync<T>(
        string key,
        Func<Task<T>> factory,
        Action<CacheEntryOptions>? configureOptions = null,
        TimeSpan? lockTimeout = null,
        CancellationToken cancellationToken = default);

    CacheStatistics GetStatistics();
}
