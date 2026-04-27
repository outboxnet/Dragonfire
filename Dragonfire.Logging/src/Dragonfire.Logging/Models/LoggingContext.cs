using System;
using System.Collections.Generic;

namespace Dragonfire.Logging.Models
{
    /// <summary>
    /// Per-request ambient context that enriches log entries with a correlation ID,
    /// optional user identity, and arbitrary custom key/value pairs.
    /// Managed by <see cref="Services.IDragonfireLoggingService"/>.
    /// </summary>
    public sealed class LoggingContext
    {
        public string CorrelationId { get; set; } = string.Empty;
        public string? UserId { get; set; }
        public Dictionary<string, object> CustomData { get; set; } = new();
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
    }
}
