using System.Collections;
using System.Collections.Generic;

namespace Dragonfire.Logging.Logging
{
    /// <summary>
    /// Scope state bag used by <see cref="Services.DragonfireLoggingService"/>.
    /// <para>
    /// Wraps a <see cref="Dictionary{TKey,TValue}"/> and overrides <see cref="ToString"/>
    /// so that the local <c>JsonConsoleFormatter</c> writes a readable <c>"Message"</c>
    /// field instead of the type name. Structured log providers (Application Insights,
    /// Seq, OpenTelemetry) read the <c>IEnumerable&lt;KeyValuePair&gt;</c> interface and
    /// receive every entry as an individual <c>customDimension</c>.
    /// </para>
    /// </summary>
    internal sealed class DragonfireScopeState : IEnumerable<KeyValuePair<string, object>>
    {
        private readonly Dictionary<string, object> _data;
        private readonly string _message;

        internal DragonfireScopeState(Dictionary<string, object> data, string message)
        {
            _data    = data;
            _message = message;
        }

        internal object this[string key]
        {
            get => _data[key];
            set => _data[key] = value;
        }

        /// <summary>Returns the human-readable log label used as the scope <c>Message</c> field.</summary>
        public override string ToString() => _message;

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => _data.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _data.GetEnumerator();
    }
}
