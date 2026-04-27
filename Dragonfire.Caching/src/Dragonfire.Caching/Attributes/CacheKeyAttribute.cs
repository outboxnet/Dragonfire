namespace Dragonfire.Caching.Attributes;

/// <summary>
/// Marks a method parameter as a component of the cache key.
/// When present, only <c>[CacheKey]</c> parameters are included in auto-generated keys.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, Inherited = true)]
public sealed class CacheKeyAttribute : Attribute
{
    /// <summary>Override the parameter name used in the cache key.</summary>
    public string? Name { get; set; }

    public CacheKeyAttribute() { }

    public CacheKeyAttribute(string name)
    {
        Name = name;
    }
}
