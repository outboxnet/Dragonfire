using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SagaNet.Persistence.EfCore.Entities;

namespace SagaNet.Persistence.EfCore.Configurations;

internal sealed class WorkflowInstanceConfiguration : IEntityTypeConfiguration<WorkflowInstanceEntity>
{
    public void Configure(EntityTypeBuilder<WorkflowInstanceEntity> builder)
    {
        builder.ToTable("WorkflowInstances");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.WorkflowName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.DataType)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(x => x.DataJson)
            .IsRequired()
            .HasColumnType("nvarchar(max)");

        builder.Property(x => x.CorrelationId)
            .HasMaxLength(200);

        builder.Property(x => x.Error)
            .HasMaxLength(2000);

        // Optimistic concurrency
        builder.Property(x => x.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        // Indexes for polling queries
        builder.HasIndex(x => new { x.Status, x.NextExecutionTime })
            .HasDatabaseName("IX_WorkflowInstances_Status_NextExecution");

        builder.HasIndex(x => x.WorkflowName)
            .HasDatabaseName("IX_WorkflowInstances_WorkflowName");

        builder.HasIndex(x => x.CorrelationId)
            .HasDatabaseName("IX_WorkflowInstances_CorrelationId");

        builder.HasMany(x => x.ExecutionPointers)
            .WithOne(x => x.WorkflowInstance)
            .HasForeignKey(x => x.WorkflowInstanceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
