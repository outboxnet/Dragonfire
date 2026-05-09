using Microsoft.EntityFrameworkCore;

namespace Dragonfire.Outbox.AzureStorageQueueSample.Domain;

/// <summary>
/// The host application's domain DbContext. The outbox publisher enlists in this context's
/// transaction so writes to <see cref="Orders"/> and the outbox row commit atomically.
/// </summary>
public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(b =>
        {
            b.ToTable("Orders", "app");
            b.HasKey(o => o.Id);
            b.Property(o => o.CustomerId).HasMaxLength(64).IsRequired();
            b.Property(o => o.Currency).HasMaxLength(8).IsRequired();
            b.Property(o => o.Total).HasPrecision(18, 2);
            b.HasIndex(o => o.CreatedAt);
        });
    }
}
