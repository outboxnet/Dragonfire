using System.Threading.Channels;
using Dragonfire.Caching.Interfaces;
using Dragonfire.Caching.Models;
using Microsoft.Extensions.Options;

namespace Dragonfire.Caching.Core;

/// <summary>
/// Channel-backed invalidation queue. Configure capacity, drop policy, and consumer
/// count via <see cref="InvalidationQueueOptions"/>.
/// </summary>
internal sealed class ChannelInvalidationQueue : IInvalidationQueue
{
    private readonly Channel<InvalidationRequest> _channel;

    public ChannelInvalidationQueue(IOptions<InvalidationQueueOptions> options)
    {
        var opts = options.Value;

        _channel = opts.Capacity.HasValue
            ? Channel.CreateBounded<InvalidationRequest>(new BoundedChannelOptions(opts.Capacity.Value)
            {
                FullMode = opts.DropWhenFull
                    ? BoundedChannelFullMode.DropOldest
                    : BoundedChannelFullMode.Wait,
                SingleWriter = false,
                SingleReader = opts.ConsumerCount == 1
            })
            : Channel.CreateUnbounded<InvalidationRequest>(new UnboundedChannelOptions
            {
                SingleWriter = false,
                SingleReader = opts.ConsumerCount == 1
            });
    }

    public ValueTask EnqueueAsync(InvalidationRequest request, CancellationToken cancellationToken = default)
        => _channel.Writer.WriteAsync(request, cancellationToken);

    internal IAsyncEnumerable<InvalidationRequest> ReadAllAsync(CancellationToken ct)
        => _channel.Reader.ReadAllAsync(ct);
}
