using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dragonfire.Features.EntityFrameworkCore.Configurations;

internal sealed class FeatureEntityTypeConfiguration : IEntityTypeConfiguration<FeatureEntity>
{
    public void Configure(EntityTypeBuilder<FeatureEntity> builder)
    {
        builder.ToTable("Features", "features");
        builder.HasKey(e => e.Name);

        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(1000);
        builder.Property(e => e.Version).IsConcurrencyToken();
        builder.Property(e => e.UpdatedAt);

        builder.HasMany(e => e.Rules)
               .WithOne()
               .HasForeignKey(r => r.FeatureName)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FeatureRuleEntityTypeConfiguration : IEntityTypeConfiguration<FeatureRuleEntity>
{
    public void Configure(EntityTypeBuilder<FeatureRuleEntity> builder)
    {
        builder.ToTable("FeatureRules", "features");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.FeatureName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.RuleType).HasMaxLength(50).IsRequired();
        builder.Property(e => e.Payload).HasMaxLength(4000).IsRequired();

        builder.HasIndex(e => new { e.FeatureName, e.Order });
    }
}

internal sealed class FeatureAuditEntityTypeConfiguration : IEntityTypeConfiguration<FeatureAuditEntity>
{
    public void Configure(EntityTypeBuilder<FeatureAuditEntity> builder)
    {
        builder.ToTable("FeatureAudit", "features");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.FeatureName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Action).HasMaxLength(50).IsRequired();
        builder.Property(e => e.Source).HasMaxLength(100).IsRequired();
        builder.Property(e => e.MetadataJson);

        builder.HasIndex(e => new { e.FeatureName, e.Timestamp });
    }
}
