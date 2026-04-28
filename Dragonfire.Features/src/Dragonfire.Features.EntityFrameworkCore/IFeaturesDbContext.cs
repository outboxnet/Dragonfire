using Microsoft.EntityFrameworkCore;

namespace Dragonfire.Features.EntityFrameworkCore;

/// <summary>
/// Marker interface that the EF source and audit log resolve via DI to find the
/// <see cref="DbContext"/> hosting the feature tables. Implement this on whichever DbContext
/// owns the schema (typically your application DbContext that called
/// <c>ApplyFeatureConfigurations</c>).
///
/// <code>
/// public sealed class AppDbContext : DbContext, IFeaturesDbContext
/// {
///     public DbSet&lt;FeatureEntity&gt; Features =&gt; Set&lt;FeatureEntity&gt;();
///     public DbSet&lt;FeatureRuleEntity&gt; FeatureRules =&gt; Set&lt;FeatureRuleEntity&gt;();
///     public DbSet&lt;FeatureAuditEntity&gt; FeatureAudit =&gt; Set&lt;FeatureAuditEntity&gt;();
/// }
/// </code>
/// </summary>
public interface IFeaturesDbContext
{
    DbSet<FeatureEntity> Features { get; }
    DbSet<FeatureRuleEntity> FeatureRules { get; }
    DbSet<FeatureAuditEntity> FeatureAudit { get; }
}
