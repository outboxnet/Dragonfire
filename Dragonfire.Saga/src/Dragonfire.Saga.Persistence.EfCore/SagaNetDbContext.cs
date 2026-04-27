using Microsoft.EntityFrameworkCore;
using Dragonfire.Saga.Persistence.EfCore.Configurations;
using Dragonfire.Saga.Persistence.EfCore.Entities;

namespace Dragonfire.Saga.Persistence.EfCore;

/// <summary>
/// EF Core DbContext for Dragonfire.Saga persistence.
/// </summary>
public sealed class SagaNetDbContext : DbContext
{
    public SagaNetDbContext(DbContextOptions<SagaNetDbContext> options) : base(options) { }

    public DbSet<WorkflowInstanceEntity> WorkflowInstances => Set<WorkflowInstanceEntity>();
    public DbSet<ExecutionPointerEntity> ExecutionPointers => Set<ExecutionPointerEntity>();
    public DbSet<WorkflowEventEntity> WorkflowEvents => Set<WorkflowEventEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new WorkflowInstanceConfiguration());
        modelBuilder.ApplyConfiguration(new ExecutionPointerConfiguration());
        modelBuilder.ApplyConfiguration(new WorkflowEventConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
