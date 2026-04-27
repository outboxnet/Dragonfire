namespace Dragonfire.Caching.Models;

/// <summary>Per-operation cache policy loaded from configuration.</summary>
public sealed class CacheOperationPolicy
{
    /// <summary>Time-to-live in seconds. Null means use the provider default.</summary>
    public int? TtlSeconds { get; set; }

    /// <summary>Tag templates applied when this operation's result is cached (e.g. <c>user:{Id}</c>).</summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>Tag templates to invalidate when this operation runs (e.g. on mutation methods).</summary>
    public List<string> InvalidatesTags { get; set; } = [];
}

/// <summary>
/// Root configuration object bound from the <c>Caching</c> config section.
/// </summary>
public sealed class CacheSettings
{
    /// <summary>Default TTL in seconds when no per-operation policy is found. Defaults to 300.</summary>
    public int DefaultTtlSeconds { get; set; } = 300;

    /// <summary>Per-operation policies keyed by <see cref="CacheOperation.Name"/>.</summary>
    public Dictionary<string, CacheOperationPolicy> Operations { get; set; } = [];
}
