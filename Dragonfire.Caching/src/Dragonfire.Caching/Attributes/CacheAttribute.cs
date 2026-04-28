namespace Dragonfire.Caching.Attributes;

/// <summary>
/// Marks a method whose return value should be cached. Read at compile time by
/// <c>Dragonfire.Caching.Generator</c> to emit a wrapper that calls <c>ICacheService.GetOrAddAsync</c>.
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
