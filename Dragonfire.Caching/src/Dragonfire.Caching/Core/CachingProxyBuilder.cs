using System.Linq.Expressions;
using System.Reflection;
using Dragonfire.Caching.Interfaces;
using Dragonfire.Caching.Strategies;

namespace Dragonfire.Caching.Core;

/// <summary>
/// Implements <see cref="ICachingBuilder{T}"/> and creates a <see cref="CachingProxy{T}"/> on build.
/// </summary>
internal sealed class CachingProxyBuilder<T> : ICachingBuilder<T> where T : class
{
    private readonly CachingProxyConfig _config = new();
    private readonly ICacheService _cache;
    private readonly ICacheKeyStrategy _keyStrategy;

    public CachingProxyBuilder(ICacheService cache, ICacheKeyStrategy keyStrategy)
    {
        _cache = cache;
        _keyStrategy = keyStrategy;
    }

    public ICachingBuilder<T> Cache<TResult>(
        Expression<Func<T, Task<TResult>>> method,
        string cacheKeyTemplate,
        TimeSpan? expiration = null)
    {
        var name = GetMethodName(method);
        _config.CacheRules[name] = new ProgrammaticCacheRule(cacheKeyTemplate, expiration);
        return this;
    }

    public ICachingBuilder<T> Invalidate(
        Expression<Func<T, Task>> method,
        string cacheKeyTemplate)
    {
        var name = GetMethodName(method);
        _config.InvalidationRules[name] = new ProgrammaticInvalidationRule(cacheKeyTemplate);
        return this;
    }

    public ICachingBuilder<T> Invalidate<TResult>(
        Expression<Func<T, Task<TResult>>> method,
        string cacheKeyTemplate)
    {
        var name = GetMethodName(method);
        _config.InvalidationRules[name] = new ProgrammaticInvalidationRule(cacheKeyTemplate);
        return this;
    }

    public T Wrap(T implementation)
        => CachingProxy<T>.Create(implementation, _cache, _keyStrategy, _config);

    private static string GetMethodName<TDelegate>(Expression<TDelegate> expr)
    {
        if (expr.Body is MethodCallExpression call)
            return call.Method.Name;

        throw new ArgumentException("Expression must be a method call.", nameof(expr));
    }
}
