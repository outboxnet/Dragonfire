namespace Dragonfire.Caching.Models;

/// <summary>
/// Identifies a named cache operation used by <see cref="Core.CacheExecutor"/> and
/// <see cref="Core.InvalidationWorker"/>. Define operations as static fields to enable
/// compile-time safety and IDE navigation.
/// </summary>
public sealed class CacheOperation
{
    public string Name { get; }

    public CacheOperation(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public override string ToString() => Name;
}
