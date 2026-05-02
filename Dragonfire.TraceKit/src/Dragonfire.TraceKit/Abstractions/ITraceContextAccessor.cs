namespace Dragonfire.TraceKit.Abstractions;

/// <summary>
/// Ambient accessor for the current <see cref="ITraceContext"/>. Returns <c>null</c>
/// when no inbound request is in flight (e.g. background work outside the middleware).
/// </summary>
public interface ITraceContextAccessor
{
    /// <summary>The trace context flowing with the current async control flow, or <c>null</c>.</summary>
    ITraceContext? Current { get; }
}
