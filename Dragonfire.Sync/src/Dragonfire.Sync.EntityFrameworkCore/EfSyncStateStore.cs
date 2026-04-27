using Microsoft.EntityFrameworkCore;
using Dragonfire.Sync.Abstractions;

namespace Dragonfire.Sync.EntityFrameworkCore;

/// <summary>
/// EF-Core backed <see cref="ISyncStateStore"/>. Persists per-stream sync state
/// into the table mapped by <see cref="SyncStateModelExtensions.ConfigureSyncState"/>.
/// </summary>
/// <typeparam name="TContext">A consumer <see cref="DbContext"/> implementing <see cref="ISyncStateDbContext"/>.</typeparam>
public sealed class EfSyncStateStore<TContext> : ISyncStateStore
    where TContext : DbContext, ISyncStateDbContext
{
    private readonly TContext _db;

    /// <inheritdoc />
    public EfSyncStateStore(TContext db) => _db = db;

    /// <inheritdoc />
    public async Task<SyncStateRecord?> GetAsync(SyncStateKey key, CancellationToken cancellationToken = default)
    {
        var row = await _db.SyncStates.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ProviderName == key.ProviderName && x.StreamName == key.StreamName, cancellationToken)
            .ConfigureAwait(false);
        return row is null ? null : ToRecord(row);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<SyncStateRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _db.SyncStates.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.ConvertAll(ToRecord);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<SyncStateRecord>> GetByProviderAsync(string providerName, CancellationToken cancellationToken = default)
    {
        var rows = await _db.SyncStates.AsNoTracking()
            .Where(x => x.ProviderName == providerName)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.ConvertAll(ToRecord);
    }

    /// <inheritdoc />
    public Task RecordRunStartedAsync(SyncStateKey key, DateTime startedAtUtc, CancellationToken cancellationToken = default) =>
        UpsertAsync(key, row =>
        {
            row.LastRunAt = startedAtUtc;
            row.LastStatus = (int)SyncStatus.Running;
        }, cancellationToken);

    /// <inheritdoc />
    public Task RecordSuccessAsync(SyncStateKey key, DateTime startedAtUtc, TimeSpan duration, int recordCount, CancellationToken cancellationToken = default) =>
        UpsertAsync(key, row =>
        {
            row.LastSuccessAt = startedAtUtc;
            row.LastRunAt = startedAtUtc + duration;
            row.LastStatus = (int)SyncStatus.Succeeded;
            row.LastDurationTicks = duration.Ticks;
            row.LastRecordCount = recordCount;
            row.LastError = null;
            row.TotalSuccesses += 1;
            row.ConsecutiveFailures = 0;
        }, cancellationToken);

    /// <inheritdoc />
    public Task RecordFailureAsync(SyncStateKey key, DateTime startedAtUtc, TimeSpan duration, Exception exception, CancellationToken cancellationToken = default) =>
        UpsertAsync(key, row =>
        {
            row.LastRunAt = startedAtUtc + duration;
            row.LastStatus = (int)SyncStatus.Failed;
            row.LastDurationTicks = duration.Ticks;
            row.LastError = Truncate(exception.Message, 4000);
            row.TotalFailures += 1;
            row.ConsecutiveFailures += 1;
        }, cancellationToken);

    /// <inheritdoc />
    public Task RecordSkippedAsync(SyncStateKey key, DateTime atUtc, string reason, CancellationToken cancellationToken = default) =>
        UpsertAsync(key, row =>
        {
            row.LastRunAt = atUtc;
            row.LastStatus = (int)SyncStatus.Skipped;
            row.LastError = Truncate(reason, 4000);
        }, cancellationToken);

    private async Task UpsertAsync(SyncStateKey key, Action<SyncStateEntity> mutate, CancellationToken cancellationToken)
    {
        var row = await _db.SyncStates
            .FirstOrDefaultAsync(x => x.ProviderName == key.ProviderName && x.StreamName == key.StreamName, cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            row = new SyncStateEntity { ProviderName = key.ProviderName, StreamName = key.StreamName };
            mutate(row);
            _db.SyncStates.Add(row);
        }
        else
        {
            mutate(row);
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another writer updated the row first. Reload and reapply once.
            var entry = _db.Entry(row);
            await entry.ReloadAsync(cancellationToken).ConfigureAwait(false);
            mutate(row);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static SyncStateRecord ToRecord(SyncStateEntity row) => new()
    {
        Key = new SyncStateKey(row.ProviderName, row.StreamName),
        LastSuccessAt = row.LastSuccessAt,
        LastRunAt = row.LastRunAt,
        LastStatus = (SyncStatus)row.LastStatus,
        LastDuration = row.LastDurationTicks is { } t ? TimeSpan.FromTicks(t) : null,
        LastRecordCount = row.LastRecordCount,
        LastError = row.LastError,
        TotalSuccesses = row.TotalSuccesses,
        TotalFailures = row.TotalFailures,
        ConsecutiveFailures = row.ConsecutiveFailures
    };

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
