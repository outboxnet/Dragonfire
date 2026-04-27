using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Dragonfire.Caching.Interfaces;
using Dragonfire.Caching.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MsCacheItemPriority = Microsoft.Extensions.Caching.Memory.CacheItemPriority;

namespace Dragonfire.Caching.Memory.Providers;

/// <summary>
/// In-process cache provider backed by <see cref="IMemoryCache"/>.
/// Supports pattern-based removal, statistics, and eviction callbacks.
/// </summary>
public sealed class MemoryCacheProvider : ICacheProvider
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<MemoryCacheProvider> _logger;
    private readonly ConcurrentDictionary<string, EntryMeta> _keyIndex = new(StringComparer.Ordinal);
    private CacheStatistics _stats = new();

    public string Name => "Memory";

    public MemoryCacheProvider(IMemoryCache cache, ILogger<MemoryCacheProvider> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(key, out T? value))
        {
            Interlocked.Increment(ref _stats.TotalHits);
            if (_keyIndex.TryGetValue(key, out var m)) m.LastAccessed = DateTimeOffset.UtcNow;
            _logger.LogDebug("Cache hit: {Key}", key);
            return Task.FromResult<T?>(value);
        }

        Interlocked.Increment(ref _stats.TotalMisses);
        _logger.LogDebug("Cache miss: {Key}", key);
        return Task.FromResult<T?>(default);
    }

    public Task SetAsync<T>(string key, T value, CacheEntryOptions options, CancellationToken cancellationToken = default)
    {
        options.Validate();

        var entry = new MemoryCacheEntryOptions
        {
            Priority = MapPriority(options.Priority),
            Size = options.Size
        };

        if (options.AbsoluteExpiration.HasValue)
            entry.AbsoluteExpiration = options.AbsoluteExpiration;
        else if (options.AbsoluteExpirationRelativeToNow.HasValue)
            entry.AbsoluteExpirationRelativeToNow = options.AbsoluteExpirationRelativeToNow;

        if (options.SlidingExpiration.HasValue)
            entry.SlidingExpiration = options.SlidingExpiration;

        entry.RegisterPostEvictionCallback((k, _, reason, _) =>
        {
            _keyIndex.TryRemove(k.ToString()!, out _);
            _logger.LogDebug("Evicted: {Key}, Reason: {Reason}", k, reason);
        });

        _cache.Set(key, value, entry);
        _keyIndex[key] = new EntryMeta { Tags = options.Tags };

        Interlocked.Increment(ref _stats.TotalSets);
        _logger.LogDebug("Cache set: {Key}", key);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _cache.Remove(key);
        _keyIndex.TryRemove(key, out _);
        Interlocked.Increment(ref _stats.TotalRemovals);
        _logger.LogDebug("Cache removed: {Key}", key);
        return Task.CompletedTask;
    }

    public Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        // Convert glob pattern (* and ?) to regex
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace(@"\*", ".*")
            .Replace(@"\?", ".") + "$";

        var regex = new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        var toRemove = _keyIndex.Keys.Where(k => regex.IsMatch(k)).ToList();

        foreach (var key in toRemove)
        {
            _cache.Remove(key);
            _keyIndex.TryRemove(key, out _);
        }

        _logger.LogDebug("Removed {Count} entries matching pattern: {Pattern}", toRemove.Count, pattern);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult(_cache.TryGetValue(key, out _));

    public async Task<T> GetOrAddAsync<T>(string key, Func<Task<T>> factory, CacheEntryOptions options, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(key, out T? cached) && cached is not null)
            return cached;

        var value = await factory();
        await SetAsync(key, value, options, cancellationToken);
        return value;
    }

    public Task RefreshAsync(string key, CancellationToken cancellationToken = default)
    {
        // IMemoryCache doesn't expose refresh; touching with TryGetValue resets sliding expiration.
        _cache.TryGetValue(key, out _);
        return Task.CompletedTask;
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
    {
        var tasks = values.Select(kvp => SetAsync(kvp.Key, kvp.Value, options, cancellationToken));
        return Task.WhenAll(tasks);
    }

    public CacheStatistics GetStatistics()
    {
        _stats.CurrentEntryCount = _keyIndex.Count;
        return _stats;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        if (_cache is MemoryCache mc)
        {
            mc.Compact(1.0);
            _keyIndex.Clear();
            _stats = new CacheStatistics();
            _logger.LogInformation("Cache cleared.");
        }
        return Task.CompletedTask;
    }

    public void Dispose() { /* IMemoryCache lifetime is managed by DI */ }

    private static MsCacheItemPriority MapPriority(Models.CacheItemPriority priority) => priority switch
    {
        Models.CacheItemPriority.Low          => MsCacheItemPriority.Low,
        Models.CacheItemPriority.High         => MsCacheItemPriority.High,
        Models.CacheItemPriority.NeverRemove  => MsCacheItemPriority.NeverRemove,
        _                                     => MsCacheItemPriority.Normal
    };

    private sealed class EntryMeta
    {
        public HashSet<string> Tags { get; init; } = [];
        public DateTimeOffset LastAccessed { get; set; } = DateTimeOffset.UtcNow;
    }
}
