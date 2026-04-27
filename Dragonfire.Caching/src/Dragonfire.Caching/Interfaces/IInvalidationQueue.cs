using Dragonfire.Caching.Models;

namespace Dragonfire.Caching.Interfaces;

/// <summary>
/// Async queue for cache invalidation requests processed by <see cref="Core.InvalidationWorker"/>.
/// </summary>
public interface IInvalidationQueue
{
    ValueTask EnqueueAsync(InvalidationRequest request, CancellationToken cancellationToken = default);
}
