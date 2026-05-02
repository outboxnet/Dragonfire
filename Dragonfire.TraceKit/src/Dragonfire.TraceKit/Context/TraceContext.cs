using System.Threading;
using Dragonfire.TraceKit.Abstractions;

namespace Dragonfire.TraceKit.Context;

/// <summary>
/// Default <see cref="ITraceContext"/>: an immutable correlation id plus an interlocked
/// counter for outbound call sequencing. Safe to share across threads and async tasks.
/// </summary>
public sealed class TraceContext : ITraceContext
{
    private int _outboundSequence;

    public TraceContext(string correlationId, string? tenantId, string? userId)
    {
        CorrelationId = correlationId;
        TenantId = tenantId;
        UserId = userId;
    }

    public string CorrelationId { get; }
    public string? TenantId { get; }
    public string? UserId { get; }

    public int NextOutboundSequence() => Interlocked.Increment(ref _outboundSequence);
}
