using Microsoft.EntityFrameworkCore;

namespace Dragonfire.Sync.EntityFrameworkCore;

/// <summary>
/// Helpers for configuring the Dragonfire.Sync state table on a consumer
/// <see cref="DbContext"/>.
/// </summary>
public static class SyncStateModelExtensions
{
    /// <summary>
    /// Map the <c>SyncStates</c> table. Call from <c>OnModelCreating</c>:
    /// <code>modelBuilder.ConfigureSyncState();</code>
    /// </summary>
    /// <param name="modelBuilder">Model being built.</param>
    /// <param name="tableName">Override the default table name (default: <c>SyncStates</c>).</param>
    /// <param name="schema">Optional schema name.</param>
    public static ModelBuilder ConfigureSyncState(this ModelBuilder modelBuilder, string tableName = "SyncStates", string? schema = null)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<SyncStateEntity>(b =>
        {
            b.ToTable(tableName, schema);
            b.HasKey(x => new { x.ProviderName, x.StreamName });
            b.Property(x => x.ProviderName).HasMaxLength(200).IsRequired();
            b.Property(x => x.StreamName).HasMaxLength(200).IsRequired();
            b.Property(x => x.LastError).HasMaxLength(4000);
            b.Property(x => x.RowVersion).IsRowVersion();
        });

        return modelBuilder;
    }
}
