using System.Threading.Channels;

namespace InboxNet.Interfaces;

/// <summary>
/// In-process channel that carries newly-received inbox message IDs to the processor.
/// The receiver enqueues each committed message ID; the hot-path loop drains the channel
/// and locks each message via a PK-seek UPDATE — no polling, sub-millisecond latency for
/// same-instance dispatch.
/// </summary>
public interface IInboxSignal
{
    void Notify(Guid messageId);
    ChannelReader<Guid> Reader { get; }
}
