using System.Threading.Channels;
using OutboxNet.Interfaces;

namespace OutboxNet.Signals;

/// <summary>
/// Bounded channel (capacity 10 000, DropOldest) that relays published message IDs to
/// the processor hot-path. DropOldest means a burst beyond capacity loses the oldest
/// hint — those messages are still in the database and will be caught by the cold-path
/// poll within <c>ColdPollingInterval</c>. No message is ever lost; only the
/// sub-millisecond delivery optimisation degrades gracefully under extreme burst.
/// </summary>
internal sealed class ChannelOutboxSignal : IOutboxSignal
{
    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(
        new BoundedChannelOptions(10_000)
        {
            FullMode    = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = true
        });

    public void Notify(Guid messageId) => _channel.Writer.TryWrite(messageId);

    public ChannelReader<Guid> Reader => _channel.Reader;
}
