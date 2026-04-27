using System.Threading.Channels;
using InboxNet.Interfaces;

namespace InboxNet.Signals;

/// <summary>
/// Bounded channel (capacity 10 000, DropOldest) that relays received message IDs to the
/// dispatcher hot-path. DropOldest means a burst beyond capacity loses the oldest hint
/// — the message is still persisted and will be picked up by the cold-path poll within
/// <c>ColdPollingInterval</c>. No message is lost; only the sub-millisecond optimisation
/// degrades under extreme burst.
/// </summary>
internal sealed class ChannelInboxSignal : IInboxSignal
{
    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(
        new BoundedChannelOptions(10_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = true
        });

    public void Notify(Guid messageId) => _channel.Writer.TryWrite(messageId);

    public ChannelReader<Guid> Reader => _channel.Reader;
}
