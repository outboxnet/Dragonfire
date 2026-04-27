namespace Dragonfire.Caching.Interfaces;

/// <summary>
/// Tracks which cache keys belong to which tags, enabling bulk invalidation.
/// Default implementation is in-process (<see cref="Core.InMemoryTagIndex"/>).
/// Use Dragonfire.Caching.Redis for a distributed implementation.
/// </summary>
public interface ITagIndex
{
    Task AddAsync(string tag, string key, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetKeysAsync(string tag, CancellationToken cancellationToken = default);
    Task RemoveTagAsync(string tag, CancellationToken cancellationToken = default);
}
