using Dragonfire.TraceKit.Models;

namespace Dragonfire.TraceKit.Abstractions;

/// <summary>
/// Hand-off point between the request hot path and the background consumer that
/// pushes traces to <see cref="ITraceRepository"/>. The default implementation writes
/// to an in-memory bounded channel and never blocks the caller.
/// </summary>
public interface ITraceWriter
{
    /// <summary>
    /// Enqueues a trace for asynchronous persistence. Returns immediately. If the queue
    /// is full, the trace is dropped (oldest first) so callers are never throttled.
    /// </summary>
    /// <returns><c>true</c> if the trace was accepted, <c>false</c> if it was dropped.</returns>
    bool TryEnqueue(ApiTrace trace);
}
