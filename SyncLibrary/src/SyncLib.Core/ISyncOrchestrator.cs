using SyncLib.Abstractions;

namespace SyncLib.Core;

/// <summary>
/// Hosted service that runs registered sync streams on a schedule. Resolved
/// from DI as a singleton; also registered as <see cref="Microsoft.Extensions.Hosting.IHostedService"/>.
/// </summary>
public interface ISyncOrchestrator
{
    /// <summary>Trigger a single run of <paramref name="key"/> immediately, outside the schedule.</summary>
    /// <exception cref="ArgumentException">No stream with this key is registered.</exception>
    Task TriggerManualSyncAsync(SyncStateKey key, CancellationToken cancellationToken = default);

    /// <summary>Composite keys of all registered streams.</summary>
    IReadOnlyCollection<SyncStateKey> RegisteredStreams { get; }
}
