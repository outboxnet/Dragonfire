using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Dragonfire.Features;

/// <summary>
/// Persists records of every change the refresh service observes. Mandatory for B2B contracts
/// that require an answer to "who turned this on for that tenant, and when?".
/// The default implementation (<see cref="Audit.NoOpFeatureAuditLog"/>) discards entries —
/// register one of the integration packages (EF Core, etc.) to persist them.
/// </summary>
public interface IFeatureAuditLog
{
    Task RecordAsync(IReadOnlyList<FeatureAuditEntry> entries, CancellationToken cancellationToken = default);
}
