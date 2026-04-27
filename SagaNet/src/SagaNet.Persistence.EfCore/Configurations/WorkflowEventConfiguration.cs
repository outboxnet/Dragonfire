using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SagaNet.Persistence.EfCore.Entities;

namespace SagaNet.Persistence.EfCore.Configurations;

internal sealed class WorkflowEventConfiguration : IEntityTypeConfiguration<WorkflowEventEntity>
{
    public void Configure(EntityTypeBuilder<WorkflowEventEntity> builder)
    {
        builder.ToTable("WorkflowEvents");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.EventName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.EventKey)
            .IsRequired()
            .HasMaxLength(500);

        builder.HasIndex(x => new { x.EventName, x.EventKey, x.IsConsumed })
            .HasDatabaseName("IX_WorkflowEvents_Name_Key_Consumed");

        builder.HasIndex(x => x.CreatedAt)
            .HasDatabaseName("IX_WorkflowEvents_CreatedAt");
    }
}
