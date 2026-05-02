namespace Dragonfire.TraceKit.Abstractions;

/// <summary>
/// Per-inbound-request context carrying the correlation id and a sequence counter
/// shared by every outbound third-party call captured while the request executes.
/// Flowed across awaits and parallel tasks via <see cref="System.Threading.AsyncLocal{T}"/>,
/// so it is safe with <c>Task.WhenAll</c> and parallel HttpClient calls.
/// </summary>
public interface ITraceContext
{
    /// <summary>The shared correlation id assigned to the inbound request and inherited by every outbound call.</summary>
    string CorrelationId { get; }

    /// <summary>Tenant id resolved by the host, if any.</summary>
    string? TenantId { get; }

    /// <summary>User id resolved by the host, if any.</summary>
    string? UserId { get; }

    /// <summary>
    /// Atomically allocates the next sequence number for an outbound call. The inbound
    /// request itself uses sequence <c>0</c>; outbound calls receive 1, 2, 3, … in the
    /// order they begin, even when started concurrently.
    /// </summary>
    int NextOutboundSequence();
}
