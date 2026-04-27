using Microsoft.EntityFrameworkCore;
using SagaNet.Persistence.EfCore.Configurations;
using SagaNet.Persistence.EfCore.Entities;

namespace SagaNet.Persistence.EfCore;

/// <summary>
/// EF Core DbContext for SagaNet persistence.
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
