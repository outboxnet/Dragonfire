using System.Threading;
using Dragonfire.TraceKit.Abstractions;

namespace Dragonfire.TraceKit.Context;

/// <summary>
/// Ambient accessor backed by an <see cref="AsyncLocal{T}"/>. The middleware writes
/// the context once on entry; outbound DelegatingHandlers (and any user code) read it
/// from anywhere in the same logical async flow, including inside parallel tasks.
/// </summary>
public sealed class TraceContextAccessor : ITraceContextAccessor
{
    private static readonly AsyncLocal<ITraceContext?> Holder = new();

    /// <inheritdoc />
    public ITraceContext? Current => Holder.Value;

    /// <summary>
    /// Pushes a context for the current async flow and returns a disposable that
    /// restores the previous value. Intended for the inbound middleware; ignore in
    /// application code.
    /// </summary>
    public IDisposable BeginScope(ITraceContext context)
    {
        var previous = Holder.Value;
        Holder.Value = context;
        return new Scope(previous);
    }

    private sealed class Scope : IDisposable
    {
        private readonly ITraceContext? _previous;
        private bool _disposed;

        public Scope(ITraceContext? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Holder.Value = _previous;
        }
    }
}
