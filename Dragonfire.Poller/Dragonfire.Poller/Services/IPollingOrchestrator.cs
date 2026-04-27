using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Dragonfire.Poller.Services
{
    // External API - Simple facade for clients
    public interface IPollingOrchestrator
    {
        Task<PollingResponse> StartPollingAsync<TRequest, TResponse>(
            string pollingType,
            TRequest request,
            PollingOptions? options = null,
            CancellationToken cancellationToken = default);

        Task<PollingStatusResponse?> GetStatusAsync(Guid pollingId, CancellationToken cancellationToken = default);

        Task<bool> CancelPollingAsync(Guid pollingId, CancellationToken cancellationToken = default);

        IAsyncEnumerable<PollingUpdateEvent> SubscribeToUpdatesAsync(
            Guid pollingId,
            CancellationToken cancellationToken = default);
    }

    // Simple DTOs for external clients
    public class PollingResponse
    {
        public Guid PollingId { get; set; }
        public string PollingType { get; set; } = "";
        public PollingStatus Status { get; set; }
        public DateTime StartedAt { get; set; }
        public string StatusUrl { get; set; } = "";
    }

    public class PollingStatusResponse
    {
        public Guid PollingId { get; set; }
        public PollingStatus Status { get; set; }
        public int Attempts { get; set; }
        public DateTime? CompletedAt { get; set; }
        public object? Result { get; set; }
        public string? FailureReason { get; set; }
        public TimeSpan? Duration { get; set; }
        public List<PollingAttemptSummary> RecentAttempts { get; set; } = new();
    }

    public class PollingAttemptSummary
    {
        public int AttemptNumber { get; set; }
        public DateTime Timestamp { get; set; }
        public bool Success { get; set; }
        public TimeSpan Duration { get; set; }
        public string? Error { get; set; }
    }

    public class PollingUpdateEvent
    {
        public Guid PollingId { get; set; }
        public PollingStatus Status { get; set; }
        public int AttemptNumber { get; set; }
        public DateTime Timestamp { get; set; }
        public string Message { get; set; } = "";
        public object? PartialResult { get; set; }
    }

    public class PollingOptions
    {
        public int? MaxAttempts { get; set; }
        public TimeSpan? InitialDelay { get; set; }
        public TimeSpan? MaxDelay { get; set; }
        public TimeSpan? Timeout { get; set; }
        public double? BackoffMultiplier { get; set; }
        public bool NotifyOnEachAttempt { get; set; } = false;
        public Dictionary<string, string>? Metadata { get; set; }
    }
}
