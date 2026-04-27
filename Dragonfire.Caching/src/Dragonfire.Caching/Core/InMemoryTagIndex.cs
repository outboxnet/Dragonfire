using System.Collections.Concurrent;
using Dragonfire.Caching.Interfaces;

namespace Dragonfire.Caching.Core;

/// <summary>
/// In-process tag index. Suitable for single-node deployments.
/// For distributed scenarios replace with <c>Dragonfire.Caching.Redis.RedisTagIndex</c>.
/// </summary>
internal sealed class InMemoryTagIndex : ITagIndex
{
    private readonly ConcurrentDictionary<string, HashSet<string>> _map = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public Task AddAsync(string tag, string key, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (!_map.TryGetValue(tag, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                _map[tag] = set;
            }
            set.Add(key);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> GetKeysAsync(string tag, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            IReadOnlyList<string> result = _map.TryGetValue(tag, out var set)
                ? set.ToList()
                : [];
            return Task.FromResult(result);
        }
    }

    public Task RemoveTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        _map.TryRemove(tag, out _);
        return Task.CompletedTask;
    }
}
