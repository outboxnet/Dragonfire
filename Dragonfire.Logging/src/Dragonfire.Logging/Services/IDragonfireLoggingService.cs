using System.Threading.Tasks;
using Dragonfire.Logging.Models;
using Microsoft.Extensions.Logging;

namespace Dragonfire.Logging.Services
{
    /// <summary>
    /// Core logging service. Receives structured <see cref="LogEntry"/> objects from
    /// the HTTP filter and service interceptors, writes them via the standard
    /// <see cref="ILogger"/> infrastructure, and optionally forwards them to a custom sink.
    /// </summary>
    public interface IDragonfireLoggingService
    {
        /// <summary>
        /// Synchronous write — used by the proxy for intercepted synchronous methods
        /// so the call never blocks on an async continuation.
        /// </summary>
        void Log(LogEntry entry);

        /// <summary>Persist a fully-built log entry asynchronously.</summary>
        Task LogAsync(LogEntry entry);

        /// <summary>Get (or lazily create) the ambient context for <paramref name="correlationId"/>.</summary>
        LoggingContext GetOrCreateContext(string correlationId);

        /// <summary>Discard the context for <paramref name="correlationId"/>.</summary>
        void ClearContext(string correlationId);
    }
}
