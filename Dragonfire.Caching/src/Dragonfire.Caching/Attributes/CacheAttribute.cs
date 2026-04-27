namespace Dragonfire.Caching.Attributes;

/// <summary>
/// Marks a method whose return value should be cached.
/// Used with the <see cref="Core.CachingProxy{T}"/> decorator.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = true)]
public sealed class CacheAttribute : Attribute
{
    /// <summary>
    /// Cache key template. Supports <c>{ParameterName}</c> placeholders.
    /// If omitted the key is auto-generated as <c>TypeName.MethodName(param=value,...)</c>.
    /// </summary>
    public string? KeyTemplate { get; set; }

    /// <summary>Absolute expiration in seconds. Mutually exclusive with <see cref="SlidingExpirationSeconds"/>.</summary>
    public int AbsoluteExpirationSeconds { get; set; }

    /// <summary>Sliding expiration in seconds. Defaults to 300 (5 min). Mutually exclusive with <see cref="AbsoluteExpirationSeconds"/>.</summary>
    public int SlidingExpirationSeconds { get; set; } = 300;

    /// <summary>Tags for group invalidation (supports <c>{ParameterName}</c> placeholders).</summary>
    public string[] Tags { get; set; } = [];

    /// <summary>When <see langword="true"/>, null factory results are also cached.</summary>
    public bool CacheNullValues { get; set; } = false;
}
