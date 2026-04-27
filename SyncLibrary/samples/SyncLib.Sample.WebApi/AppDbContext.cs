using Microsoft.EntityFrameworkCore;
using SyncLib.EntityFrameworkCore;
using SyncLib.Sample.WebApi.Sync;

namespace SyncLib.Sample.WebApi;

public sealed class AppDbContext : DbContext, ISyncStateDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<ForecastEntity> Forecasts => Set<ForecastEntity>();
    public DbSet<AlertEntity> Alerts => Set<AlertEntity>();

    public DbSet<SyncStateEntity> SyncStates => Set<SyncStateEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ForecastEntity>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.City).HasMaxLength(100).IsRequired();
            b.HasIndex(x => x.City).IsUnique();
        });

        modelBuilder.Entity<AlertEntity>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.City).HasMaxLength(100).IsRequired();
            b.Property(x => x.Severity).HasMaxLength(20).IsRequired();
            b.Property(x => x.Message).HasMaxLength(1000).IsRequired();
        });

        modelBuilder.ConfigureSyncState();
    }
}
