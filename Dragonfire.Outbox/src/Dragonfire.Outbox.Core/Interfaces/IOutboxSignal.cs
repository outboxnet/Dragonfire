using System.Threading.Channels;

namespace Dragonfire.Outbox.Interfaces;

/// <summary>
/// In-process channel that carries published message IDs to the processor.
/// Publishers enqueue each message ID after their transaction commits.
/// The processor hot-path drains the channel and locks each message individually
/// via a PK-seek UPDATE — no polling, no batch scan, sub-millisecond latency.
/// </summary>
public interface IOutboxSignal
{
    /// <summary>Enqueues a message ID for immediate hot-path processing.</summary>
    void Notify(Guid messageId);

    /// <summary>Async-enumerable reader consumed by the processor hot-path loop.</summary>
    ChannelReader<Guid> Reader { get; }
}
