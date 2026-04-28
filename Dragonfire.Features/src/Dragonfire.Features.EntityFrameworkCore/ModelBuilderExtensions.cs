using System;
using Dragonfire.Features.EntityFrameworkCore.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Dragonfire.Features.EntityFrameworkCore;

/// <summary>
/// Convenience for plugging the feature tables into your own <see cref="DbContext"/>.
///
/// <code>
/// protected override void OnModelCreating(ModelBuilder modelBuilder)
/// {
///     base.OnModelCreating(modelBuilder);
///     modelBuilder.ApplyFeatureConfigurations();
/// }
/// </code>
/// </summary>
public static class ModelBuilderExtensions
{
    public static ModelBuilder ApplyFeatureConfigurations(this ModelBuilder modelBuilder)
    {
        if (modelBuilder is null) throw new ArgumentNullException(nameof(modelBuilder));

        modelBuilder.ApplyConfiguration(new FeatureEntityTypeConfiguration());
        modelBuilder.ApplyConfiguration(new FeatureRuleEntityTypeConfiguration());
        modelBuilder.ApplyConfiguration(new FeatureAuditEntityTypeConfiguration());

        return modelBuilder;
    }
}
