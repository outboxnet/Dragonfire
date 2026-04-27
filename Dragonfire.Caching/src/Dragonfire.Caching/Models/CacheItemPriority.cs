namespace Dragonfire.Caching.Models;

/// <summary>Eviction priority for memory-backed providers.</summary>
public enum CacheItemPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    NeverRemove = 3
}
