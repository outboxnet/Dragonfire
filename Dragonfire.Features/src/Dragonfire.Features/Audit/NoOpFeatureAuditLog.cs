using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Dragonfire.Features.Audit;

/// <summary>
/// Default audit log that drops every entry. Replace with a persisting implementation
/// (e.g. EF Core, append-only file, external SIEM) when compliance requires it.
/// </summary>
public sealed class NoOpFeatureAuditLog : IFeatureAuditLog
{
    public Task RecordAsync(IReadOnlyList<FeatureAuditEntry> entries, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
