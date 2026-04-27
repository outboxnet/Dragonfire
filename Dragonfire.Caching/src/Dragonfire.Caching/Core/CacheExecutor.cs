using Dragonfire.Caching.Interfaces;
using Dragonfire.Caching.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dragonfire.Caching.Core;

/// <summary>
/// Configuration-driven cache executor. Define operations in <c>appsettings.json</c> under
/// the <c>Caching:Operations</c> section, then use <see cref="GetOrCreateAsync{T}"/> and
/// <see cref="ExecuteAndInvalidateAsync"/> in your services.
/// </summary>
public sealed class CacheExecutor
{
    private readonly ICacheProvider _cache;
    private readonly ITemplateResolver _resolver;
    private readonly ITagIndex _tagIndex;
    private readonly IInvalidationQueue _queue;
    private readonly CacheSettings _settings;
    private readonly ILogger<CacheExecutor> _logger;

    public CacheExecutor(
        ICacheProvider cache,
        ITemplateResolver resolver,
        ITagIndex tagIndex,
        IInvalidationQueue queue,
        IOptions<CacheSettings> settings,
        ILogger<CacheExecutor> logger)
    {
        _cache = cache;
        _resolver = resolver;
        _tagIndex = tagIndex;
        _queue = queue;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Get a cached value or call <paramref name="factory"/> and store the result.
    /// Tags defined in the operation policy are registered in <see cref="ITagIndex"/>.
    /// </summary>
    public async Task<T> GetOrCreateAsync<T>(
        CacheOperation operation,
        object? parameters,
        Func<Task<T>> factory,
        CancellationToken ct = default)
    {
        var key = BuildKey(operation, parameters);

        var cached = await _cache.GetAsync<T>(key, ct);
        if (cached is not null)
        {
            _logger.LogDebug("Cache hit for operation '{Op}', key='{Key}'.", operation.Name, key);
            return cached;
        }

        _logger.LogDebug("Cache miss for operation '{Op}', key='{Key}'.", operation.Name, key);

        var result = await factory();

        if (result is null) return result!;

        var ttl = GetTtl(operation);
        await _cache.SetAsync(key, result, CacheEntryOptions.Absolute(ttl), ct);

        foreach (var tagTemplate in GetTags(operation))
        {
            var tag = _resolver.Resolve(tagTemplate, parameters);
            await _tagIndex.AddAsync(tag, key, ct);
        }

        return result;
    }

    /// <summary>
    /// Execute <paramref name="action"/> and enqueue an invalidation request so that the
    /// <see cref="InvalidationWorker"/> removes all affected tagged entries asynchronously.
    /// </summary>
    public async Task ExecuteAndInvalidateAsync(
        CacheOperation operation,
        object? parameters,
        Func<Task> action,
        CancellationToken ct = default)
    {
        await action();
        await _queue.EnqueueAsync(new InvalidationRequest(operation.Name, parameters), ct);
    }

    private TimeSpan GetTtl(CacheOperation op)
    {
        if (_settings.Operations.TryGetValue(op.Name, out var policy) && policy.TtlSeconds.HasValue)
            return TimeSpan.FromSeconds(policy.TtlSeconds.Value);

        return TimeSpan.FromSeconds(_settings.DefaultTtlSeconds);
    }

    private IEnumerable<string> GetTags(CacheOperation op)
    {
        if (_settings.Operations.TryGetValue(op.Name, out var policy))
            return policy.Tags;

        return [];
    }

    private static string BuildKey(CacheOperation op, object? parameters)
    {
        if (parameters is null) return op.Name;

        var props = parameters.GetType().GetProperties();
        var parts = props.Select(p => $"{p.Name}={p.GetValue(parameters) ?? "null"}");
        return $"{op.Name}|{string.Join("|", parts)}";
    }
}
