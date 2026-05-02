using System.Threading.Channels;
using Dragonfire.TraceKit.Abstractions;
using Dragonfire.TraceKit.Models;
using Dragonfire.TraceKit.Options;
using Microsoft.Extensions.Options;

namespace Dragonfire.TraceKit.Writers;

/// <summary>
/// Bounded-channel writer with <see cref="BoundedChannelFullMode.DropOldest"/>: enqueue is
/// O(1), wait-free in the steady state, and never blocks the request hot path. When the
/// channel is full the oldest pending trace is discarded — telemetry is best-effort by
/// design and must not throttle production traffic.
/// </summary>
public sealed class ChannelTraceWriter : ITraceWriter
{
    private readonly Channel<ApiTrace> _channel;

    public ChannelTraceWriter(IOptions<TraceKitOptions> options)
    {
        var capacity = Math.Max(1, options.Value.ChannelCapacity);
        _channel = Channel.CreateBounded<ApiTrace>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    }

    /// <summary>The reader consumed by the background hosted service.</summary>
    internal ChannelReader<ApiTrace> Reader => _channel.Reader;

    /// <inheritdoc />
    public bool TryEnqueue(ApiTrace trace)
        => _channel.Writer.TryWrite(trace);
}
