using Dragonfire.Caching.Interfaces;
using Dragonfire.Caching.Models;
using Microsoft.Extensions.Logging;

namespace Dragonfire.Caching.Core;

/// <summary>
/// Default implementation of <see cref="ICacheService"/>. Delegates storage to
/// <see cref="ICacheProvider"/> and tag management to <see cref="ITagIndex"/>.
/// </summary>
internal sealed class CacheService : ICacheService
{
    private readonly ICacheProvider _provider;
    private readonly ITagIndex _tagIndex;
    private readonly CacheLockManager _lockManager;
    private readonly ILogger<CacheService> _logger;

    public string ProviderName => _provider.Name;

    public CacheService(
        ICacheProvider provider,
        ITagIndex tagIndex,
        CacheLockManager lockManager,
        ILogger<CacheService> logger)
    {
        _provider = provider;
        _tagIndex = tagIndex;
        _lockManager = lockManager;
        _logger = logger;
    }

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        => _provider.GetAsync<T>(key, cancellationToken);

    public Task SetAsync<T>(string key, T value, Action<CacheEntryOptions>? configureOptions = null, CancellationToken cancellationToken = default)
    {
        var opts = new CacheEntryOptions();
        configureOptions?.Invoke(opts);
        return _provider.SetAsync(key, value, opts, cancellationToken);
    }

    public async Task<T> GetOrAddAsync<T>(string key, Func<Task<T>> factory, Action<CacheEntryOptions>? configureOptions = null, CancellationToken cancellationToken = default)
    {
        var cached = await _provider.GetAsync<T>(key, cancellationToken);
        if (cached is not null)
            return cached;

        var opts = new CacheEntryOptions();
        configureOptions?.Invoke(opts);

        var value = await factory();
        await _provider.SetAsync(key, value, opts, cancellationToken);
        return value;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        => _provider.RemoveAsync(key, cancellationToken);

    public Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
        => _provider.RemoveByPatternAsync(pattern, cancellationToken);

    public async Task<T> GetOrAddWithTagsAsync<T>(
        string key,
        Func<Task<T>> factory,
        IEnumerable<string> tags,
        Action<CacheEntryOptions>? configureOptions = null,
        CancellationToken cancellationToken = default)
    {
        var cached = await _provider.GetAsync<T>(key, cancellationToken);
        if (cached is not null)
            return cached;

        var opts = new CacheEntryOptions();
        configureOptions?.Invoke(opts);

        var value = await factory();
        await _provider.SetAsync(key, value, opts, cancellationToken);

        foreach (var tag in tags)
            await _tagIndex.AddAsync(tag, key, cancellationToken);

        return value;
    }

    public async Task InvalidateByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        var keys = await _tagIndex.GetKeysAsync(tag, cancellationToken);

        foreach (var key in keys)
            await _provider.RemoveAsync(key, cancellationToken);

        await _tagIndex.RemoveTagAsync(tag, cancellationToken);

        _logger.LogDebug("Invalidated {Count} entries under tag '{Tag}'.", keys.Count, tag);
    }

    public async Task<T> GetOrAddWithLockAsync<T>(
        string key,
        Func<Task<T>> factory,
        Action<CacheEntryOptions>? configureOptions = null,
        TimeSpan? lockTimeout = null,
        CancellationToken cancellationToken = default)
    {
        using var cts = lockTimeout.HasValue
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;
        cts?.CancelAfter(lockTimeout!.Value);

        var ct = cts?.Token ?? cancellationToken;

        using var handle = await _lockManager.AcquireAsync(key, ct);
        return await GetOrAddAsync(key, factory, configureOptions, cancellationToken);
    }

    public CacheStatistics GetStatistics() => _provider.GetStatistics();
}
