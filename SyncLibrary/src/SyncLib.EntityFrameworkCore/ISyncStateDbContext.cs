using Microsoft.EntityFrameworkCore;

namespace SyncLib.EntityFrameworkCore;

/// <summary>
/// Marker interface for a <see cref="DbContext"/> that hosts the SyncLib state table.
/// Implement on your application's <c>DbContext</c> and call
/// <see cref="SyncStateModelExtensions.ConfigureSyncState"/> from <c>OnModelCreating</c>.
/// </summary>
public interface ISyncStateDbContext
{
    /// <summary>The sync-state table.</summary>
    DbSet<SyncStateEntity> SyncStates { get; }

    /// <summary>Persist staged changes.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
