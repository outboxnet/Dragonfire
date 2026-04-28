using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dragonfire.Features.EntityFrameworkCore;

/// <summary>
/// Persists audit entries to the <c>features.FeatureAudit</c> table via the application's
/// <see cref="IFeaturesDbContext"/>. Resolves a fresh DbContext per write so the refresh
/// service can call this from any thread without sharing state.
/// </summary>
public sealed class EfCoreFeatureAuditLog : IFeatureAuditLog
{
    private readonly IServiceScopeFactory _scopeFactory;

    public EfCoreFeatureAuditLog(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task RecordAsync(IReadOnlyList<FeatureAuditEntry> entries, CancellationToken cancellationToken = default)
    {
        if (entries.Count == 0) return;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IFeaturesDbContext>();

        foreach (var entry in entries)
        {
            dbContext.FeatureAudit.Add(new FeatureAuditEntity
            {
                FeatureName     = entry.FeatureName,
                Action          = entry.Action.ToString(),
                Timestamp       = entry.Timestamp,
                Source          = entry.Source,
                PreviousVersion = entry.PreviousVersion,
                CurrentVersion  = entry.CurrentVersion,
                MetadataJson    = entry.Metadata.Count == 0 ? null : JsonSerializer.Serialize(entry.Metadata),
            });
        }

        // The DbContext casts to its concrete type via the saving call site — the marker interface
        // doesn't expose SaveChangesAsync, so we round-trip through the concrete DbContext.
        await ((DbContext)dbContext).SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
