using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Dragonfire.Poller.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Dragonfire.Poller.Services
{

    // Thread-safe repository using ConcurrentDictionary
    public class ConcurrentPollingRepository<TRequestData, TResponseData> : IPollingRepository<TRequestData, TResponseData>
    {
        private readonly ConcurrentDictionary<Guid, PollingRequest<TRequestData, TResponseData>> _storage = new();
        private readonly SemaphoreSlim _cleanupLock = new(1, 1);
        private readonly TimeSpan _retentionPeriod;
        private readonly ILogger<ConcurrentPollingRepository<TRequestData, TResponseData>> _logger;
        private Timer? _cleanupTimer;

        public ConcurrentPollingRepository(
            IOptions<PollingServiceConfiguration> configuration,
            ILogger<ConcurrentPollingRepository<TRequestData, TResponseData>> logger)
        {
            _retentionPeriod = configuration.Value.DataRetentionPeriod ?? TimeSpan.FromHours(24);
            _logger = logger;
            StartCleanupTimer();
        }

        public Task<PollingRequest<TRequestData, TResponseData>?> GetAsync(Guid id)
        {
            _storage.TryGetValue(id, out var request);
            return Task.FromResult(request?.Clone()); // Return clone for thread safety
        }

        public Task SaveAsync(PollingRequest<TRequestData, TResponseData> request)
        {
            var clone = request.Clone();
            _storage.TryAdd(request.Id, clone);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(PollingRequest<TRequestData, TResponseData> request)
        {
            var clone = request.Clone();
            _storage.AddOrUpdate(request.Id, clone, (_, _) => clone);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id)
        {
            _storage.TryRemove(id, out _);
            return Task.CompletedTask;
        }

        public Task<IEnumerable<PollingRequest<TRequestData, TResponseData>>> GetActivePollingsAsync()
        {
            var active = _storage.Values
                .Where(r => r.Status == PollingStatus.Pending || r.Status == PollingStatus.Polling)
                .Select(r => r.Clone())
                .ToList();

            return Task.FromResult<IEnumerable<PollingRequest<TRequestData, TResponseData>>>(active);
        }

        private void StartCleanupTimer()
        {
            _cleanupTimer = new Timer(CleanupOldRequests, null, _retentionPeriod, _retentionPeriod);
        }

        private async void CleanupOldRequests(object? state)
        {
            await _cleanupLock.WaitAsync();
            try
            {
                var cutoff = DateTime.UtcNow - _retentionPeriod;
                var oldRequests = _storage.Values
                    .Where(r => r.CreatedAt < cutoff &&
                               (r.Status == PollingStatus.Completed ||
                                r.Status == PollingStatus.Failed ||
                                r.Status == PollingStatus.TimedOut))
                    .ToList();

                foreach (var request in oldRequests)
                {
                    _storage.TryRemove(request.Id, out _);
                }

                if (oldRequests.Any())
                {
                    _logger.LogInformation("Cleaned up {Count} old polling requests", oldRequests.Count);
                }
            }
            finally
            {
                _cleanupLock.Release();
            }
        }
    }

    public static class PollingRequestExtensions
    {
        public static PollingRequest<TRequest, TResponse> Clone<TRequest, TResponse>(
            this PollingRequest<TRequest, TResponse> request)
        {
            return new PollingRequest<TRequest, TResponse>
            {
                Id = request.Id,
                RequestData = request.RequestData,
                CreatedAt = request.CreatedAt,
                CompletedAt = request.CompletedAt,
                Configuration = request.Configuration,
                Status = request.Status,
                Attempts = request.Attempts?.ToList() ?? new List<PollingAttempt>(),
                Result = request.Result,
                FailureReason = request.FailureReason
            };
        }
    }
}
