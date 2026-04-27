using Dragonfire.Caching.Interfaces;
using StackExchange.Redis;

namespace Dragonfire.Caching.Redis.Core;

/// <summary>
/// Distributed tag index backed by Redis Sets.
/// Each tag is stored as a Redis Set where members are the associated cache keys.
/// Register via <c>services.AddDragonfireRedisTagIndex()</c>.
/// </summary>
public sealed class RedisTagIndex : ITagIndex
{
    private readonly IDatabase _db;

    public RedisTagIndex(IConnectionMultiplexer redis)
    {
        _db = redis.GetDatabase();
    }

    private static RedisKey TagKey(string tag) => $"dragonfire:tag:{tag}";

    public Task AddAsync(string tag, string key, CancellationToken cancellationToken = default)
        => _db.SetAddAsync(TagKey(tag), key);

    public async Task<IReadOnlyList<string>> GetKeysAsync(string tag, CancellationToken cancellationToken = default)
    {
        var members = await _db.SetMembersAsync(TagKey(tag));
        return members.Select(m => (string)m!).ToList();
    }

    public Task RemoveTagAsync(string tag, CancellationToken cancellationToken = default)
        => _db.KeyDeleteAsync(TagKey(tag));
}
