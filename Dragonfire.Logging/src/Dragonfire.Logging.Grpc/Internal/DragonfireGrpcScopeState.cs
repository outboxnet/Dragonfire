using System.Collections;
using System.Collections.Generic;

namespace Dragonfire.Logging.Grpc.Internal
{
    /// <summary>
    /// Scope bag for gRPC log entries.
    /// Wraps a string-keyed dictionary and overrides <see cref="ToString"/> so that
    /// <c>JsonConsoleFormatter</c> and other formatters show a readable human label
    /// (e.g. <c>"[Dragonfire:gRPC] Greeter.SayHello"</c>) in the <c>Message</c> field
    /// instead of the type name.
    /// </summary>
    internal sealed class DragonfireGrpcScopeState
        : IEnumerable<KeyValuePair<string, object?>>
    {
        private readonly Dictionary<string, object?> _data =
            new Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);

        private readonly string _message;

        internal DragonfireGrpcScopeState(string message) => _message = message;

        internal object? this[string key]
        {
            get => _data.TryGetValue(key, out var v) ? v : null;
            set => _data[key] = value;
        }

        public override string ToString() => _message;

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _data.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _data.GetEnumerator();
    }
}
