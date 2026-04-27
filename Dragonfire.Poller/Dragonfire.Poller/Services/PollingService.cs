using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Dragonfire.Poller.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Dragonfire.Poller.Services
{
    public class PollingService<TRequestData, TResponseData> : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IPollingMetricsTracker _metricsTracker;
        private readonly ILogger<PollingService<TRequestData, TResponseData>> _logger;
        private readonly Channel<PollingRequest<TRequestData, TResponseData>> _queue;
        private readonly ConcurrentDictionary<Guid, PollingRequestState> _activePollings;
        private readonly SemaphoreSlim _throttler;
        private readonly ReaderWriterLockSlim _repositoryLock;
        private readonly IOptions<PollingServiceConfiguration> _configuration;

        // Thread-safe counter for metrics
        private long _totalProcessedCount;
        private long _totalSuccessCount;
        private long _totalFailureCount;

        public PollingService(
            IServiceProvider serviceProvider,
            IPollingMetricsTracker metricsTracker,
            ILogger<PollingService<TRequestData, TResponseData>> logger,
            IOptions<PollingServiceConfiguration> configuration)
        {
            _serviceProvider = serviceProvider;
            _metricsTracker = metricsTracker;
            _logger = logger;
            _configuration = configuration;

            _queue = Channel.CreateBounded<PollingRequest<TRequestData, TResponseData>>(
                new BoundedChannelOptions(configuration.Value.QueueCapacity)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleWriter = false,  // Allow multiple writers
                    SingleReader = true     // Single consumer for ordering
                });

            _activePollings = new ConcurrentDictionary<Guid, PollingRequestState>();
            _throttler = new SemaphoreSlim(configuration.Value.MaxConcurrentPollings);
            _repositoryLock = new ReaderWriterLockSlim();
        }

        public async Task<Guid> EnqueuePollingAsync(
            TRequestData requestData,
            PollingConfiguration? configuration = null,
            CancellationToken cancellationToken = default)
        {
            var request = new PollingRequest<TRequestData, TResponseData>
            {
                Id = Guid.NewGuid(),
                RequestData = requestData,
                CreatedAt = DateTime.UtcNow,
                Configuration = configuration ?? new PollingConfiguration(),
                Status = PollingStatus.Pending,
                Attempts = new List<PollingAttempt>() // Thread-safe list will be created per request
            };

            // Thread-safe write to channel
            await _queue.Writer.WriteAsync(request, cancellationToken);

            // Thread-safe metrics update
            _metricsTracker.RecordQueueLength(_queue.Reader.Count);
            Interlocked.Increment(ref _totalProcessedCount);

            return request.Id;
        }

        public async Task<PollingRequest<TRequestData, TResponseData>?> GetPollingStatusAsync(Guid id)
        {
            // Thread-safe read with lock
            _repositoryLock.EnterReadLock();
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IPollingRepository<TRequestData, TResponseData>>();
                return await repository.GetAsync(id);
            }
            finally
            {
                _repositoryLock.ExitReadLock();
            }
        }

        public async Task<bool> CancelPollingAsync(Guid id)
        {
            // Thread-safe cancellation
            if (_activePollings.TryGetValue(id, out var state))
            {
                var cts = state.CancellationTokenSource;
                if (cts != null && !cts.IsCancellationRequested)
                {
                    await Task.Run(() => cts.Cancel());
                    return true;
                }
            }
            return false;
        }

        public async Task<bool> StopPollingAsync(Guid id)
        {
            // Thread-safe removal
            if (_activePollings.TryRemove(id, out var state))
            {
                state.CancellationTokenSource?.Cancel();
                state.CancellationTokenSource?.Dispose();
                return true;
            }
            return false;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Polling Service Started with {MaxConcurrent} concurrent capacity",
                _configuration.Value.MaxConcurrentPollings);

            // Single consumer pattern for thread safety
            await foreach (var request in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                _ = Task.Run(() => ProcessPollingRequestAsync(request, stoppingToken));
            }
        }

        private async Task ProcessPollingRequestAsync(
            PollingRequest<TRequestData, TResponseData> request,
            CancellationToken stoppingToken)
        {
            // Thread-safe throttling
            await _throttler.WaitAsync(stoppingToken);

            var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            cts.CancelAfter(request.Configuration.Timeout);

            var state = new PollingRequestState
            {
                RequestId = request.Id,
                CancellationTokenSource = cts,
                StartTime = DateTime.UtcNow
            };

            // Thread-safe addition to active pollings
            if (!_activePollings.TryAdd(request.Id, state))
            {
                _logger.LogWarning("Failed to add polling request {RequestId} to active dictionary", request.Id);
                _throttler.Release();
                cts.Dispose();
                return;
            }

            try
            {
                await ProcessWithRetriesAsync(request, cts.Token);
            }
            catch (OperationCanceledException)
            {
                await UpdateRequestStatusAsync(request, PollingStatus.TimedOut, "Polling operation timed out");
                _metricsTracker.RecordPollingFailed(typeof(TRequestData).Name, "Timeout");
                Interlocked.Increment(ref _totalFailureCount);
            }
            catch (Exception ex)
            {
                await UpdateRequestStatusAsync(request, PollingStatus.Failed, ex.Message);
                _metricsTracker.RecordPollingFailed(typeof(TRequestData).Name, "Exception");
                Interlocked.Increment(ref _totalFailureCount);
                _logger.LogError(ex, "Polling failed for request {RequestId}", request.Id);
            }
            finally
            {
                // Thread-safe cleanup
                _activePollings.TryRemove(request.Id, out _);
                _throttler.Release();
                cts.Dispose();
            }
        }

        private async Task ProcessWithRetriesAsync(
            PollingRequest<TRequestData, TResponseData> request,
            CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var strategy = scope.ServiceProvider.GetRequiredService<IPollingStrategy<TRequestData, TResponseData>>();
            var condition = scope.ServiceProvider.GetRequiredService<IPollingCondition<TResponseData>>();

            await UpdateRequestStatusAsync(request, PollingStatus.Polling, null);

            var currentDelay = request.Configuration.InitialDelay;
            var startTime = DateTime.UtcNow;

            // Thread-safe attempt tracking
            var attempts = new ConcurrentBag<PollingAttempt>();

            for (int attempt = 1; attempt <= request.Configuration.MaxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var attemptStart = DateTime.UtcNow;

                try
                {
                    var pollingResult = await strategy.PollAsync(request.RequestData, cancellationToken);
                    var attemptDuration = DateTime.UtcNow - attemptStart;

                    var pollingAttempt = new PollingAttempt
                    {
                        AttemptNumber = attempt,
                        StartTime = attemptStart,
                        EndTime = DateTime.UtcNow,
                        IsSuccess = pollingResult.IsSuccess,
                        ErrorMessage = pollingResult.ErrorMessage
                    };

                    attempts.Add(pollingAttempt);

                    // Thread-safe metrics update
                    _metricsTracker.RecordPollingAttempt(
                        typeof(TRequestData).Name,
                        attempt,
                        attemptDuration,
                        pollingResult.IsSuccess);

                    // Domain-specific metrics
                    if (pollingResult.IsSuccess && pollingResult.Data != null)
                    {
                        _metricsTracker.RecordDomainMetric(typeof(TRequestData).Name, "PollingDataReceived",
                            new Dictionary<string, double> { ["DataSize"] = 1 });
                    }

                    if (pollingResult.IsSuccess && condition.IsComplete(pollingResult.Data!))
                    {
                        await CompleteRequestAsync(request, pollingResult.Data!, attempts, DateTime.UtcNow - startTime);
                        Interlocked.Increment(ref _totalSuccessCount);
                        return;
                    }

                    if (!pollingResult.IsSuccess && !pollingResult.ShouldContinue)
                    {
                        await FailRequestAsync(request, pollingResult.ErrorMessage ?? "Polling condition failed permanently", attempts);
                        Interlocked.Increment(ref _totalFailureCount);
                        return;
                    }

                    if (pollingResult.IsSuccess && condition.IsFailed(pollingResult.Data!))
                    {
                        await FailRequestAsync(request, "Domain-specific failure condition met", attempts);
                        Interlocked.Increment(ref _totalFailureCount);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    attempts.Add(new PollingAttempt
                    {
                        AttemptNumber = attempt,
                        StartTime = attemptStart,
                        EndTime = DateTime.UtcNow,
                        IsSuccess = false,
                        ErrorMessage = ex.Message
                    });

                    _logger.LogWarning(ex, "Polling attempt {Attempt} failed for request {RequestId}", attempt, request.Id);
                }

                if (attempt < request.Configuration.MaxAttempts)
                {
                    await Task.Delay(currentDelay, cancellationToken);
                    currentDelay = TimeSpan.FromMilliseconds(
                        Math.Min(currentDelay.TotalMilliseconds * request.Configuration.BackoffMultiplier,
                                 request.Configuration.MaxDelay.TotalMilliseconds));
                }
            }

            await FailRequestAsync(request, $"Maximum attempts ({request.Configuration.MaxAttempts}) reached", attempts);
            Interlocked.Increment(ref _totalFailureCount);
        }

        private async Task UpdateRequestStatusAsync(
            PollingRequest<TRequestData, TResponseData> request,
            PollingStatus status,
            string? failureReason)
        {
            // Thread-safe update with lock
            _repositoryLock.EnterWriteLock();
            try
            {
                request.Status = status;
                request.FailureReason = failureReason;

                using var scope = _serviceProvider.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IPollingRepository<TRequestData, TResponseData>>();
                await repository.UpdateAsync(request);
            }
            finally
            {
                _repositoryLock.ExitWriteLock();
            }
        }

        private async Task CompleteRequestAsync(
            PollingRequest<TRequestData, TResponseData> request,
            TResponseData result,
            ConcurrentBag<PollingAttempt> attempts,
            TimeSpan totalDuration)
        {
            _repositoryLock.EnterWriteLock();
            try
            {
                request.Status = PollingStatus.Completed;
                request.CompletedAt = DateTime.UtcNow;
                request.Result = result;
                request.Attempts = attempts.ToList();

                using var scope = _serviceProvider.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IPollingRepository<TRequestData, TResponseData>>();
                await repository.UpdateAsync(request);

                _metricsTracker.RecordPollingCompleted(typeof(TRequestData).Name, totalDuration);
                _metricsTracker.RecordDomainMetric(typeof(TRequestData).Name, "PollingSuccess",
                    new Dictionary<string, double>
                    {
                        ["Duration"] = totalDuration.TotalSeconds,
                        ["AttemptCount"] = attempts.Count
                    });
            }
            finally
            {
                _repositoryLock.ExitWriteLock();
            }
        }

        private async Task FailRequestAsync(
            PollingRequest<TRequestData, TResponseData> request,
            string failureReason,
            ConcurrentBag<PollingAttempt> attempts)
        {
            _repositoryLock.EnterWriteLock();
            try
            {
                request.Status = PollingStatus.Failed;
                request.CompletedAt = DateTime.UtcNow;
                request.FailureReason = failureReason;
                request.Attempts = attempts.ToList();

                using var scope = _serviceProvider.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IPollingRepository<TRequestData, TResponseData>>();
                await repository.UpdateAsync(request);

                _metricsTracker.RecordDomainMetric(typeof(TRequestData).Name, "PollingFailure",
                    new Dictionary<string, double>
                    {
                        ["AttemptCount"] = attempts.Count,
                        ["FailureType"] = 1
                    });
            }
            finally
            {
                _repositoryLock.ExitWriteLock();
            }
        }

        // Thread-safe statistics
        public (long Processed, long Success, long Failure, int Active) GetStatistics()
        {
            return (
                Interlocked.Read(ref _totalProcessedCount),
                Interlocked.Read(ref _totalSuccessCount),
                Interlocked.Read(ref _totalFailureCount),
                _activePollings.Count
            );
        }
    }

    // Thread-safe state holder
    internal class PollingRequestState
    {
        public Guid RequestId { get; set; }
        public CancellationTokenSource? CancellationTokenSource { get; set; }
        public DateTime StartTime { get; set; }
        public int RetryCount { get; set; }
    }

}
