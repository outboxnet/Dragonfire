namespace Dragonfire.Caching.Attributes;

/// <summary>
/// When applied to a method, invalidates cache entries after the method completes.
/// May be applied multiple times to invalidate several keys/tags.
/// Used with the <see cref="Core.CachingProxy{T}"/> decorator.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = true, AllowMultiple = true)]
public sealed class CacheInvalidateAttribute : Attribute
{
    /// <summary>
    /// Cache key pattern to invalidate. Supports <c>{ParameterName}</c> placeholders
    /// and glob wildcard <c>*</c> (e.g. <c>user:{userId}:*</c>).
    /// </summary>
    public string? KeyPattern { get; set; }

    /// <summary>Tag to invalidate (all keys sharing this tag are removed).</summary>
    public string? Tag { get; set; }

    /// <summary>
    /// When <see langword="true"/>, invalidation happens <em>before</em> method execution.
    /// Defaults to <see langword="false"/> (after execution).
    /// </summary>
    public bool InvalidateBefore { get; set; } = false;

    public CacheInvalidateAttribute(string keyPattern)
    {
        KeyPattern = keyPattern;
    }

    /// <summary>Convenience constructor: generates a pattern for <c>{entityType}:{entityIdParameter}</c>.</summary>
    public CacheInvalidateAttribute(string entityType, string entityIdParameter)
    {
        KeyPattern = $"{entityType}:{{{entityIdParameter}}}:*";
        Tag = entityType;
    }
}
