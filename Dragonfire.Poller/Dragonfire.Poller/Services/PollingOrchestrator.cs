using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Dragonfire.Poller.Services
{
    /// <summary>
    /// Default implementation of <see cref="IPollingOrchestrator"/>.
    /// Delegates to the typed <see cref="PollingService{TRequestData,TResponseData}"/> instances
    /// registered in the DI container and routes status / cancel calls via
    /// <see cref="PollingHandlerRegistry"/> — no reflection or <c>dynamic</c> required.
    /// </summary>
    public class PollingOrchestrator : IPollingOrchestrator
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IPollingMetricsTracker _metricsTracker;
        private readonly PollingHandlerRegistry _registry;
        private readonly ILogger<PollingOrchestrator> _logger;
        private readonly ConcurrentDictionary<Guid, List<Func<PollingUpdateEvent, Task>>> _subscribers = new();

        public PollingOrchestrator(
            IServiceProvider serviceProvider,
            IPollingMetricsTracker metricsTracker,
            PollingHandlerRegistry registry,
            ILogger<PollingOrchestrator> logger)
        {
            _serviceProvider = serviceProvider;
            _metricsTracker = metricsTracker;
            _registry = registry;
            _logger = logger;
        }

        public async Task<PollingResponse> StartPollingAsync<TRequest, TResponse>(
            string pollingType,
            TRequest request,
            PollingOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var pollingService = _serviceProvider.GetRequiredService<PollingService<TRequest, TResponse>>();

            var config = new PollingConfiguration
            {
                MaxAttempts      = options?.MaxAttempts      ?? 30,
                InitialDelay     = options?.InitialDelay     ?? TimeSpan.FromSeconds(1),
                MaxDelay         = options?.MaxDelay         ?? TimeSpan.FromSeconds(30),
                Timeout          = options?.Timeout          ?? TimeSpan.FromMinutes(5),
                BackoffMultiplier = options?.BackoffMultiplier ?? 2.0
            };

            var pollingId = await pollingService.EnqueuePollingAsync(request, config, cancellationToken);

            // Register typed handlers — no reflection, no dynamic
            _registry.Register(
                pollingId,
                async ct =>
                {
                    var data = await pollingService.GetPollingStatusAsync(pollingId);
                    return data is null ? null : BuildStatusResponse(pollingId, data);
                },
                ct => pollingService.CancelPollingAsync(pollingId));

            if (options?.Metadata is { Count: > 0 } metadata)
            {
                _metricsTracker.RecordDomainMetric(pollingType, "PollingMetadata",
                    metadata.ToDictionary(kvp => kvp.Key, _ => 1.0));
            }

            _metricsTracker.RecordPollingStarted(pollingType);

            if (options?.NotifyOnEachAttempt == true)
                _ = MonitorPollingProgressAsync(pollingId, pollingType, cancellationToken);

            return new PollingResponse
            {
                PollingId   = pollingId,
                PollingType = pollingType,
                Status      = PollingStatus.Pending,
                StartedAt   = DateTime.UtcNow,
                StatusUrl   = $"/api/polling/{pollingId}"
            };
        }

        public Task<PollingStatusResponse?> GetStatusAsync(
            Guid pollingId,
            CancellationToken cancellationToken = default)
            => _registry.GetStatusAsync(pollingId, cancellationToken);

        public async Task<bool> CancelPollingAsync(
            Guid pollingId,
            CancellationToken cancellationToken = default)
        {
            var cancelled = await _registry.CancelAsync(pollingId, cancellationToken);

            if (cancelled)
            {
                await NotifySubscribersAsync(pollingId, new PollingUpdateEvent
                {
                    PollingId = pollingId,
                    Status    = PollingStatus.Cancelled,
                    Timestamp = DateTime.UtcNow,
                    Message   = "Polling cancelled"
                });
            }

            return cancelled;
        }

        public async IAsyncEnumerable<PollingUpdateEvent> SubscribeToUpdatesAsync(
            Guid pollingId,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var channel = Channel.CreateUnbounded<PollingUpdateEvent>();

            Task Handler(PollingUpdateEvent update)
                => channel.Writer.WriteAsync(update, cancellationToken).AsTask();

            _subscribers.AddOrUpdate(
                pollingId,
                _ => new List<Func<PollingUpdateEvent, Task>> { Handler },
                (_, list) => { list.Add(Handler); return list; });

            try
            {
                var status = await GetStatusAsync(pollingId, cancellationToken);
                if (status is not null)
                {
                    yield return new PollingUpdateEvent
                    {
                        PollingId = pollingId,
                        Status    = status.Status,
                        Timestamp = DateTime.UtcNow,
                        Message   = $"Current status: {status.Status}"
                    };
                }

                await foreach (var update in channel.Reader.ReadAllAsync(cancellationToken))
                {
                    yield return update;

                    if (update.Status is PollingStatus.Completed or PollingStatus.Failed
                        or PollingStatus.TimedOut or PollingStatus.Cancelled)
                        break;
                }
            }
            finally
            {
                if (_subscribers.TryGetValue(pollingId, out var handlers))
                {
                    handlers.Remove(Handler);
                    if (handlers.Count == 0)
                        _subscribers.TryRemove(pollingId, out _);
                }
                channel.Writer.TryComplete();
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static PollingStatusResponse BuildStatusResponse<TReq, TResp>(
            Guid pollingId,
            PollingRequest<TReq, TResp> data)
        {
            var duration = data.CompletedAt.HasValue
                ? data.CompletedAt.Value - data.CreatedAt
                : (TimeSpan?)null;

            return new PollingStatusResponse
            {
                PollingId     = pollingId,
                Status        = data.Status,
                Attempts      = data.Attempts?.Count ?? 0,
                CompletedAt   = data.CompletedAt,
                Result        = data.Result,
                FailureReason = data.FailureReason,
                Duration      = duration,
                RecentAttempts = data.Attempts?
                    .OrderByDescending(a => a.AttemptNumber)
                    .Take(5)
                    .Select(a => new PollingAttemptSummary
                    {
                        AttemptNumber = a.AttemptNumber,
                        Timestamp     = a.EndTime,
                        Success       = a.IsSuccess,
                        Duration      = a.Duration,
                        Error         = a.ErrorMessage
                    })
                    .ToList() ?? new()
            };
        }

        private async Task MonitorPollingProgressAsync(
            Guid pollingId,
            string pollingType,
            CancellationToken cancellationToken)
        {
            DateTime lastUpdate = DateTime.MinValue;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(1000, cancellationToken);

                    var status = await GetStatusAsync(pollingId, cancellationToken);
                    if (status is null) break;

                    if (status.RecentAttempts is { Count: > 0 })
                    {
                        var latest = status.RecentAttempts[0];
                        if (latest.Timestamp > lastUpdate)
                        {
                            await NotifySubscribersAsync(pollingId, new PollingUpdateEvent
                            {
                                PollingId     = pollingId,
                                Status        = status.Status,
                                AttemptNumber = latest.AttemptNumber,
                                Timestamp     = latest.Timestamp,
                                Message       = latest.Success
                                    ? "Attempt succeeded"
                                    : $"Attempt failed: {latest.Error}",
                                PartialResult = status.Result
                            });

                            lastUpdate = latest.Timestamp;
                        }
                    }

                    if (status.Status is PollingStatus.Completed or PollingStatus.Failed
                        or PollingStatus.TimedOut or PollingStatus.Cancelled)
                        break;
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error monitoring polling progress for {PollingId}", pollingId);
                    break;
                }
            }
        }

        private async Task NotifySubscribersAsync(Guid pollingId, PollingUpdateEvent update)
        {
            if (_subscribers.TryGetValue(pollingId, out var handlers) && handlers.Count > 0)
            {
                var tasks = handlers.Select(h => h(update));
                await Task.WhenAll(tasks);
            }
        }
    }
}
