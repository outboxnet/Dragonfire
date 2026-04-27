using System.Collections.Concurrent;
using Dragonfire.Caching.Interfaces;
using Dragonfire.Caching.Models;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Dragonfire.Caching.Distributed.Providers;

/// <summary>
/// Cache provider backed by <see cref="IDistributedCache"/>.
/// Works with any registered backend: Redis (StackExchange.Redis or
/// Microsoft.Extensions.Caching.StackExchangeRedis), SQL Server, etc.
///
/// <para>
/// <b>Pattern removal:</b> <see cref="IDistributedCache"/> has no native pattern-scan API.
/// This provider maintains an in-process key index for pattern operations. In a multi-node
/// deployment, prefer tag-based invalidation via <see cref="Dragonfire.Caching.Redis.Core.RedisTagIndex"/>.
/// </para>
/// </summary>
public sealed class DistributedCacheProvider : ICacheProvider
{
    private readonly IDistributedCache _cache;
    private readonly ICacheSerializer _serializer;
    private readonly ILogger<DistributedCacheProvider> _logger;
    private readonly ConcurrentDictionary<string, byte> _keyIndex = new(StringComparer.Ordinal);
    private CacheStatistics _stats = new();

    public string Name => $"Distributed ({_cache.GetType().Name})";

    public DistributedCacheProvider(
        IDistributedCache cache,
        ICacheSerializer serializer,
        ILogger<DistributedCacheProvider> logger)
    {
        _cache = cache;
        _serializer = serializer;
        _logger = logger;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var bytes = await _cache.GetAsync(key, cancellationToken);
            if (bytes is not null)
            {
                Interlocked.Increment(ref _stats.TotalHits);
                _logger.LogDebug("Cache hit: {Key}", key);
                return _serializer.Deserialize<T>(bytes);
            }

            Interlocked.Increment(ref _stats.TotalMisses);
            _logger.LogDebug("Cache miss: {Key}", key);
            return default;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting key: {Key}", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, CacheEntryOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            options.Validate();

            var dist = new DistributedCacheEntryOptions();

            if (options.AbsoluteExpiration.HasValue)
                dist.AbsoluteExpiration = options.AbsoluteExpiration;
            else if (options.AbsoluteExpirationRelativeToNow.HasValue)
                dist.AbsoluteExpirationRelativeToNow = options.AbsoluteExpirationRelativeToNow;

            if (options.SlidingExpiration.HasValue)
                dist.SlidingExpiration = options.SlidingExpiration;

            await _cache.SetAsync(key, _serializer.Serialize(value), dist, cancellationToken);
            _keyIndex[key] = 0;

            Interlocked.Increment(ref _stats.TotalSets);
            _logger.LogDebug("Cache set: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting key: {Key}", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await _cache.RemoveAsync(key, cancellationToken);
            _keyIndex.TryRemove(key, out _);
            Interlocked.Increment(ref _stats.TotalRemovals);
            _logger.LogDebug("Cache removed: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing key: {Key}", key);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Uses an in-process key index — effective only when all writes go through this instance.
    /// For distributed scenarios use tag-based invalidation.
    /// </remarks>
    public async Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace(@"\*", ".*")
            .Replace(@"\?", ".") + "$";

        var regex = new System.Text.RegularExpressions.Regex(
            regexPattern,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.Compiled);

        var toRemove = _keyIndex.Keys.Where(k => regex.IsMatch(k)).ToList();

        foreach (var key in toRemove)
            await RemoveAsync(key, cancellationToken);

        _logger.LogDebug("Removed {Count} entries matching pattern: {Pattern}", toRemove.Count, pattern);
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _cache.GetAsync(key, cancellationToken) is not null;
        }
        catch
        {
            return false;
        }
    }

    public async Task<T> GetOrAddAsync<T>(string key, Func<Task<T>> factory, CacheEntryOptions options, CancellationToken cancellationToken = default)
    {
        var cached = await GetAsync<T>(key, cancellationToken);
        if (cached is not null) return cached;

        var value = await factory();
        await SetAsync(key, value, options, cancellationToken);
        return value;
    }

    public async Task RefreshAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await _cache.RefreshAsync(key, cancellationToken);
            _logger.LogDebug("Cache refreshed: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing key: {Key}", key);
        }
    }

    public async Task<IDictionary<string, T?>> GetMultipleAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, T?>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            var val = await GetAsync<T>(key, cancellationToken);
            if (val is not null) result[key] = val;
        }
        return result;
    }

    public Task SetMultipleAsync<T>(IDictionary<string, T> values, CacheEntryOptions options, CancellationToken cancellationToken = default)
        => Task.WhenAll(values.Select(kvp => SetAsync(kvp.Key, kvp.Value, options, cancellationToken)));

    public CacheStatistics GetStatistics()
    {
        _stats.CurrentEntryCount = _keyIndex.Count;
        return _stats;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("ClearAsync is not supported by all IDistributedCache backends.");
        return Task.CompletedTask;
    }

    public void Dispose() { }
}
