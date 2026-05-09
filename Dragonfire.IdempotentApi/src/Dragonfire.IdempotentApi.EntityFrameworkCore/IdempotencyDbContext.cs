using Dragonfire.IdempotentApi.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Dragonfire.IdempotentApi.EntityFrameworkCore;

/// <summary>
/// Stand-alone DbContext for cases where idempotency lives in its own database. Most
/// applications instead apply <see cref="ModelBuilderExtensions.ApplyIdempotentApiConfigurations"/>
/// to their existing <see cref="DbContext"/>.
/// </summary>
public class IdempotencyDbContext : DbContext
{
    public IdempotencyDbContext(DbContextOptions<IdempotencyDbContext> options) : base(options) { }

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyIdempotentApiConfigurations();
    }
}
