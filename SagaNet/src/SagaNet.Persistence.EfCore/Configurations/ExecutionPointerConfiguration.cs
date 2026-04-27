using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SagaNet.Persistence.EfCore.Entities;

namespace SagaNet.Persistence.EfCore.Configurations;

internal sealed class ExecutionPointerConfiguration : IEntityTypeConfiguration<ExecutionPointerEntity>
{
    public void Configure(EntityTypeBuilder<ExecutionPointerEntity> builder)
    {
        builder.ToTable("ExecutionPointers");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.StepName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.EventName)
            .HasMaxLength(200);

        builder.Property(x => x.EventKey)
            .HasMaxLength(500);

        builder.Property(x => x.Error)
            .HasMaxLength(2000);

        builder.Property(x => x.PersistenceDataJson)
            .IsRequired()
            .HasColumnType("nvarchar(max)");

        builder.HasIndex(x => x.WorkflowInstanceId)
            .HasDatabaseName("IX_ExecutionPointers_WorkflowInstanceId");

        builder.HasIndex(x => new { x.EventName, x.EventKey })
            .HasDatabaseName("IX_ExecutionPointers_EventName_EventKey");
    }
}
