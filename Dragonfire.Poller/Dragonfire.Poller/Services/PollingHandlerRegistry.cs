using System.Collections.Concurrent;

namespace Dragonfire.Poller.Services
{
    /// <summary>
    /// Maintains a per-polling-ID lookup of status fetchers and cancellers,
    /// enabling the orchestrator to route <c>GetStatus</c> and <c>Cancel</c>
    /// calls to the correct typed <see cref="PollingService{TRequestData,TResponseData}"/>
    /// without requiring runtime reflection or <c>dynamic</c>.
    /// </summary>
    public sealed class PollingHandlerRegistry
    {
        private readonly ConcurrentDictionary<Guid, Func<CancellationToken, Task<PollingStatusResponse?>>> _statusFetchers = new();
        private readonly ConcurrentDictionary<Guid, Func<CancellationToken, Task<bool>>> _cancellers = new();

        public void Register(
            Guid pollingId,
            Func<CancellationToken, Task<PollingStatusResponse?>> statusFetcher,
            Func<CancellationToken, Task<bool>> canceller)
        {
            _statusFetchers[pollingId] = statusFetcher;
            _cancellers[pollingId] = canceller;
        }

        public Task<PollingStatusResponse?> GetStatusAsync(Guid pollingId, CancellationToken ct) =>
            _statusFetchers.TryGetValue(pollingId, out var fetcher)
                ? fetcher(ct)
                : Task.FromResult<PollingStatusResponse?>(null);

        public Task<bool> CancelAsync(Guid pollingId, CancellationToken ct) =>
            _cancellers.TryGetValue(pollingId, out var canceller)
                ? canceller(ct)
                : Task.FromResult(false);

        public void Unregister(Guid pollingId)
        {
            _statusFetchers.TryRemove(pollingId, out _);
            _cancellers.TryRemove(pollingId, out _);
        }
    }
}
