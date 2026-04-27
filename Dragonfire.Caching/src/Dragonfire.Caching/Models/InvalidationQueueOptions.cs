namespace Dragonfire.Caching.Models;

/// <summary>
/// Controls the behaviour of the <see cref="Core.ChannelInvalidationQueue"/>.
/// Register via <c>services.Configure&lt;InvalidationQueueOptions&gt;(...)</c> or use
/// the builder helpers on <see cref="Extensions.DragonfireCachingBuilder"/>.
/// </summary>
public sealed class InvalidationQueueOptions
{
    /// <summary>
    /// Maximum number of queued invalidation requests.
    /// <c>null</c> (default) creates an unbounded queue.
    /// </summary>
    public int? Capacity { get; set; }

    /// <summary>
    /// When the queue is at capacity:
    /// <see langword="true"/> silently drops new items,
    /// <see langword="false"/> (default) applies back-pressure (waits for space).
    /// </summary>
    public bool DropWhenFull { get; set; } = false;

    /// <summary>
    /// Number of parallel consumers draining the queue. Defaults to 1.
    /// Increase if invalidation throughput is a bottleneck.
    /// </summary>
    public int ConsumerCount { get; set; } = 1;
}
