using Dragonfire.Caching.Distributed.Providers;
using Dragonfire.Caching.Interfaces;
using Dragonfire.Caching.Memory.Providers;
using Dragonfire.Caching.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dragonfire.Caching.Hybrid.Providers;

/// <summary>
/// Two-tier cache provider: L1 = in-process <see cref="MemoryCacheProvider"/>,
/// L2 = <see cref="DistributedCacheProvider"/>.
///
/// <list type="bullet">
///   <item>Reads check L1 first; on L1 miss the value is fetched from L2 and promoted to L1.</item>
///   <item>Writes go to both tiers atomically (parallel).</item>
///   <item>Removes and clears propagate to both tiers.</item>
/// </list>
///
/// DI uses <see cref="FromKeyedServicesAttribute"/> so that the two inner providers do
/// not conflict with the <see cref="ICacheProvider"/> registration for <see cref="HybridCacheProvider"/>.
/// </summary>
public sealed class HybridCacheProvider : ICacheProvider
{
    private readonly ICacheProvider _l1;
    private readonly ICacheProvider _l2;
    private readonly ILogger<HybridCacheProvider> _logger;

    public string Name => $"Hybrid (L1={_l1.Name}, L2={_l2.Name})";

    public HybridCacheProvider(
        [FromKeyedServices(HybridCacheKeys.L1)] ICacheProvider l1,
        [FromKeyedServices(HybridCacheKeys.L2)] ICacheProvider l2,
        ILogger<HybridCacheProvider> logger)
    {
        _l1 = l1;
        _l2 = l2;
        _logger = logger;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var l1Value = await _l1.GetAsync<T>(key, cancellationToken);
        if (l1Value is not null)
        {
            _logger.LogDebug("L1 hit: {Key}", key);
            return l1Value;
        }

        var l2Value = await _l2.GetAsync<T>(key, cancellationToken);
        if (l2Value is not null)
        {
            _logger.LogDebug("L2 hit: {Key}, promoting to L1", key);
            await _l1.SetAsync(key, l2Value, CacheEntryOptions.Default, cancellationToken);
            return l2Value;
        }

        _logger.LogDebug("Cache miss: {Key}", key);
        return default;
    }

    public Task SetAsync<T>(string key, T value, CacheEntryOptions options, CancellationToken cancellationToken = default)
        => Task.WhenAll(
            _l1.SetAsync(key, value, options, cancellationToken),
            _l2.SetAsync(key, value, options, cancellationToken));

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        => Task.WhenAll(
            _l1.RemoveAsync(key, cancellationToken),
            _l2.RemoveAsync(key, cancellationToken));

    public Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
        => Task.WhenAll(
            _l1.RemoveByPatternAsync(pattern, cancellationToken),
            _l2.RemoveByPatternAsync(pattern, cancellationToken));

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        => await _l1.ExistsAsync(key, cancellationToken) ||
           await _l2.ExistsAsync(key, cancellationToken);

    public async Task<T> GetOrAddAsync<T>(string key, Func<Task<T>> factory, CacheEntryOptions options, CancellationToken cancellationToken = default)
    {
        var cached = await GetAsync<T>(key, cancellationToken);
        if (cached is not null) return cached;

        var value = await factory();
        await SetAsync(key, value, options, cancellationToken);
        return value;
    }

    public Task RefreshAsync(string key, CancellationToken cancellationToken = default)
        => Task.WhenAll(
            _l1.RefreshAsync(key, cancellationToken),
            _l2.RefreshAsync(key, cancellationToken));

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
        var s1 = _l1.GetStatistics();
        var s2 = _l2.GetStatistics();
        return new CacheStatistics
        {
            TotalHits      = s1.TotalHits + s2.TotalHits,
            TotalMisses    = s1.TotalMisses + s2.TotalMisses,
            TotalSets      = s1.TotalSets + s2.TotalSets,
            TotalRemovals  = s1.TotalRemovals + s2.TotalRemovals,
            CurrentEntryCount = s1.CurrentEntryCount
        };
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
        => Task.WhenAll(
            _l1.ClearAsync(cancellationToken),
            _l2.ClearAsync(cancellationToken));

    public void Dispose()
    {
        _l1.Dispose();
        _l2.Dispose();
    }
}

/// <summary>Service-key constants used when registering L1/L2 providers via keyed DI.</summary>
public static class HybridCacheKeys
{
    public const string L1 = "Dragonfire.Hybrid.L1";
    public const string L2 = "Dragonfire.Hybrid.L2";
}
