using Dragonfire.Caching.Attributes;
using Dragonfire.Caching.Interfaces;
using Dragonfire.Caching.Models;
using Dragonfire.Caching.Strategies;
using System.Collections.Concurrent;
using System.Reflection;

namespace Dragonfire.Caching.Core;

/// <summary>
/// DispatchProxy-based caching decorator. Used internally by <see cref="CachingProxyBuilder{T}"/>.
/// </summary>
internal class CachingProxy<T> : DispatchProxy where T : class
{
    private T _target = default!;
    private ICacheService _cache = default!;
    private ICacheKeyStrategy _keyStrategy = default!;
    private CachingProxyConfig _config = default!;

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null) return null;
        args ??= [];

        var cacheAttr = Attribute.GetCustomAttribute(targetMethod,
            typeof(CacheAttribute),
            inherit: true) as CacheAttribute;

        var invalidateAttrs = targetMethod.GetCustomAttributes<CacheInvalidateAttribute>().ToArray();

        if (cacheAttr is not null)
            return InvokeWithCache(targetMethod, args, cacheAttr);

        if (invalidateAttrs.Length > 0)
            return InvokeWithInvalidation(targetMethod, args, invalidateAttrs);

        // Check programmatic rules (from CachingProxyBuilder)
        if (_config.CacheRules.TryGetValue(targetMethod.Name, out var cacheRule))
            return InvokeWithProgrammaticCache(targetMethod, args, cacheRule);

        if (_config.InvalidationRules.TryGetValue(targetMethod.Name, out var invalidationRule))
            return InvokeWithProgrammaticInvalidation(targetMethod, args, invalidationRule);

        return targetMethod.Invoke(_target, args);
    }

    private object? InvokeWithCache(MethodInfo method, object?[] args, CacheAttribute attr)
    {
        var key = _keyStrategy.GenerateKey(method, args, attr.KeyTemplate);

        var expiration = attr.AbsoluteExpirationSeconds > 0
            ? TimeSpan.FromSeconds(attr.AbsoluteExpirationSeconds)
            : TimeSpan.FromSeconds(attr.SlidingExpirationSeconds);

        var useAbsolute = attr.AbsoluteExpirationSeconds > 0;

        if (IsGenericTask(method.ReturnType, out var resultType))
        {
            return GetOrAddGenericTaskAsync(resultType!, key, method, args, expiration, useAbsolute, attr);
        }

        if (method.ReturnType == typeof(Task))
        {
            // Non-generic Task: cache bypass (nothing to return)
            return targetMethod_Invoke(method, args);
        }

        // Synchronous: sync-over-async (acceptable at proxy boundary only)
        return _cache.GetOrAddAsync(
                key,
                () => Task.FromResult(targetMethod_Invoke(method, args)),
                o => Configure(o, expiration, useAbsolute, attr.Tags, method, args))
            .GetAwaiter().GetResult();
    }

    private object? InvokeWithInvalidation(MethodInfo method, object?[] args, CacheInvalidateAttribute[] attrs)
    {
        foreach (var attr in attrs.Where(a => a.InvalidateBefore))
            RunInvalidation(attr, method, args).GetAwaiter().GetResult();

        var result = targetMethod_Invoke(method, args);

        var afterAttrs = attrs.Where(a => !a.InvalidateBefore).ToArray();
        if (afterAttrs.Length == 0) return result;

        if (result is Task task)
        {
            return task.ContinueWith(async _ =>
            {
                foreach (var attr in afterAttrs)
                    await RunInvalidation(attr, method, args);
            }, TaskScheduler.Default).Unwrap();
        }

        foreach (var attr in afterAttrs)
            RunInvalidation(attr, method, args).GetAwaiter().GetResult();

        return result;
    }

    private object? InvokeWithProgrammaticCache(MethodInfo method, object?[] args, ProgrammaticCacheRule rule)
    {
        var key = _keyStrategy.GenerateKey(method, args, rule.KeyTemplate);
        var expiration = rule.Expiration ?? TimeSpan.FromMinutes(5);

        if (IsGenericTask(method.ReturnType, out var resultType))
        {
            return GetOrAddGenericTaskAsync(resultType!, key, method, args, expiration, true, null);
        }

        return _cache.GetOrAddAsync(
                key,
                () => Task.FromResult(targetMethod_Invoke(method, args)),
                o => o.AbsoluteExpirationRelativeToNow = expiration)
            .GetAwaiter().GetResult();
    }

    private object? InvokeWithProgrammaticInvalidation(MethodInfo method, object?[] args, ProgrammaticInvalidationRule rule)
    {
        var result = targetMethod_Invoke(method, args);

        var pattern = _keyStrategy.GeneratePattern(method, args, rule.KeyTemplate);

        if (result is Task task)
        {
            return task.ContinueWith(_ => _cache.RemoveByPatternAsync(pattern), TaskScheduler.Default).Unwrap();
        }

        _cache.RemoveByPatternAsync(pattern).GetAwaiter().GetResult();
        return result;
    }

    private async Task RunInvalidation(CacheInvalidateAttribute attr, MethodInfo method, object?[] args)
    {
        if (!string.IsNullOrEmpty(attr.Tag))
            await _cache.InvalidateByTagAsync(attr.Tag);

        if (!string.IsNullOrEmpty(attr.KeyPattern))
        {
            var pattern = _keyStrategy.GeneratePattern(method, args, attr.KeyPattern);
            await _cache.RemoveByPatternAsync(pattern);
        }
    }

    // Trampoline to bypass the override interception when calling the inner target.
    private object? targetMethod_Invoke(MethodInfo method, object?[] args)
        => method.Invoke(_target, args);

    private object GetOrAddGenericTaskAsync(Type resultType, string key, MethodInfo method, object?[] args, TimeSpan expiration, bool absolute, CacheAttribute? attr)
    {
        return typeof(CachingProxy<T>)
            .GetMethod(nameof(GetOrAddAsync), BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(resultType)
            .Invoke(this, [key, method, args, expiration, absolute, attr])!;
    }

    private async Task<TResult> GetOrAddAsync<TResult>(string key, MethodInfo method, object?[] args, TimeSpan expiration, bool absolute, CacheAttribute? attr)
    {
        return await _cache.GetOrAddAsync<TResult>(
            key,
            () => (Task<TResult>)method.Invoke(_target, args)!,
            o => Configure(o, expiration, absolute, attr?.Tags ?? [], method, args));
    }

    private void Configure(CacheEntryOptions opts, TimeSpan expiration, bool absolute, string[] tagTemplates, MethodInfo method, object?[] args)
    {
        if (absolute)
            opts.AbsoluteExpirationRelativeToNow = expiration;
        else
            opts.SlidingExpiration = expiration;

        foreach (var template in tagTemplates)
        {
            var tag = _keyStrategy.GeneratePattern(method, args, template);
            opts.Tags.Add(tag);
        }
    }

    private static bool IsGenericTask(Type type, out Type? resultType)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
        {
            resultType = type.GetGenericArguments()[0];
            return true;
        }
        resultType = null;
        return false;
    }

    internal static T Create(T target, ICacheService cache, ICacheKeyStrategy keyStrategy, CachingProxyConfig config)
    {
        var proxy = Create<T, CachingProxy<T>>();
        var impl = (CachingProxy<T>)(object)proxy;
        impl._target = target;
        impl._cache = cache;
        impl._keyStrategy = keyStrategy;
        impl._config = config;
        return proxy;
    }
}

internal sealed record ProgrammaticCacheRule(string? KeyTemplate, TimeSpan? Expiration);
internal sealed record ProgrammaticInvalidationRule(string KeyTemplate);

internal sealed class CachingProxyConfig
{
    public Dictionary<string, ProgrammaticCacheRule> CacheRules { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, ProgrammaticInvalidationRule> InvalidationRules { get; } = new(StringComparer.Ordinal);
}
