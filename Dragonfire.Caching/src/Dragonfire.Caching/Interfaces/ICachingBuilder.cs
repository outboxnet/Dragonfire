using System.Linq.Expressions;

namespace Dragonfire.Caching.Interfaces;

/// <summary>
/// Fluent API for declaratively configuring caching on a service interface without attributes.
/// </summary>
/// <typeparam name="T">The service interface type.</typeparam>
public interface ICachingBuilder<T> where T : class
{
    /// <summary>Cache the result of an async method.</summary>
    ICachingBuilder<T> Cache<TResult>(
        Expression<Func<T, Task<TResult>>> method,
        string cacheKeyTemplate,
        TimeSpan? expiration = null);

    /// <summary>Invalidate a cache key pattern when an async void method completes.</summary>
    ICachingBuilder<T> Invalidate(
        Expression<Func<T, Task>> method,
        string cacheKeyTemplate);

    /// <summary>Invalidate a cache key pattern when an async method completes.</summary>
    ICachingBuilder<T> Invalidate<TResult>(
        Expression<Func<T, Task<TResult>>> method,
        string cacheKeyTemplate);
}
