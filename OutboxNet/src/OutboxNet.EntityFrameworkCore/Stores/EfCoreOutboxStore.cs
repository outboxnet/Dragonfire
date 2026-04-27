using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OutboxNet.Interfaces;
using OutboxNet.Models;
using OutboxNet.Options;

namespace OutboxNet.EntityFrameworkCore.Stores;

internal sealed class EfCoreOutboxStore : IOutboxStore
{
    private readonly OutboxDbContext _dbContext;
    private readonly OutboxOptions _options;
    private readonly ILogger<EfCoreOutboxStore> _logger;

    public EfCoreOutboxStore(
        OutboxDbContext dbContext,
        IOptions<OutboxOptions> options,
        ILogger<EfCoreOutboxStore> logger)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SaveMessageAsync(OutboxMessage message, CancellationToken ct = default)
    {
        _dbContext.OutboxMessages.Add(message);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<OutboxMessage>> LockNextBatchAsync(
       int batchSize,
       TimeSpan visibilityTimeout,
       string lockedBy,
       IReadOnlySet<Guid>? skipIds = null,
       CancellationToken ct = default)
    {
        var schema = _options.SchemaName;
        var visibilityTimeoutSeconds = (int)visibilityTimeout.TotalSeconds;

        var tenantFilterClause = _options.TenantFilter is not null
            ? "AND m.[TenantId] = @tenantFilter"
            : string.Empty;

        // Exclude IDs currently being processed by the hot path on this instance.
        // Prevents a cold-path lock attempt from racing against a concurrent hot-path
        // TryLockByIdAsync for the same row. The DB lock gate (UPDLOCK + READPAST)
        // already makes dual delivery impossible, but this eliminates the wasted
        // round-trip entirely.
        var skipIdsClause = skipIds is { Count: > 0 }
            ? "AND m.[Id] NOT IN (SELECT CAST([value] AS uniqueidentifier) FROM OPENJSON(@skipJson))"
            : string.Empty;

        // Null-safe partition equality: (a = b) OR (a IS NULL AND b IS NULL)
        var orderingClause = _options.EnableOrderedProcessing
            ? $"""

              AND (
                (m.[TenantId] IS NULL AND m.[UserId] IS NULL AND m.[EntityId] IS NULL)
                OR NOT EXISTS (
                    SELECT 1 FROM [{schema}].[OutboxMessages] m2 WITH (READCOMMITTEDLOCK)
                    WHERE m2.[Status] = @processingStatus
                      AND m2.[LockedUntil] > SYSDATETIMEOFFSET()
                      AND (m2.[TenantId] = m.[TenantId] OR (m2.[TenantId] IS NULL AND m.[TenantId] IS NULL))
                      AND (m2.[UserId]   = m.[UserId]   OR (m2.[UserId]   IS NULL AND m.[UserId]   IS NULL))
                      AND (m2.[EntityId] = m.[EntityId] OR (m2.[EntityId] IS NULL AND m.[EntityId] IS NULL))
                      AND m2.[Id] != m.[Id]
                )
              )
              """
            : string.Empty;

        // Use a CTE to guarantee ORDER BY is respected when selecting the batch.
        // Plain UPDATE TOP(n) ... ORDER BY does not guarantee which rows are picked.
        var sql = $"""
            WITH Candidates AS (
                SELECT TOP (@batchSize) m.[Id]
                FROM [{schema}].[OutboxMessages] m WITH (UPDLOCK, READPAST)
                WHERE m.[Status] IN (@pendingStatus, @processingStatus)
                  AND (m.[LockedUntil] IS NULL OR m.[LockedUntil] < SYSDATETIMEOFFSET())
                  AND (m.[NextRetryAt] IS NULL OR m.[NextRetryAt] <= SYSDATETIMEOFFSET())
                  {tenantFilterClause}
                  {skipIdsClause}
                {orderingClause}
                ORDER BY m.[CreatedAt]
            )
            UPDATE m
            SET
                m.[Status] = @processingStatus,
                m.[LockedUntil] = DATEADD(SECOND, @visibilityTimeoutSeconds, SYSDATETIMEOFFSET()),
                m.[LockedBy] = @lockedBy
            OUTPUT
                INSERTED.[Id],
                INSERTED.[EventType],
                INSERTED.[Payload],
                INSERTED.[CorrelationId],
                INSERTED.[TraceId],
                INSERTED.[Status],
                INSERTED.[RetryCount],
                INSERTED.[CreatedAt],
                INSERTED.[ProcessedAt],
                INSERTED.[LockedUntil],
                INSERTED.[LockedBy],
                INSERTED.[NextRetryAt],
                INSERTED.[LastError],
                INSERTED.[Headers],
                INSERTED.[TenantId],
                INSERTED.[UserId],
                INSERTED.[EntityId]
            FROM [{schema}].[OutboxMessages] m
            INNER JOIN Candidates c ON c.[Id] = m.[Id]
            """;

        // Build parameter list conditionally: @tenantFilter must NOT be declared when
        // tenantFilterClause is empty — sp_executesql rejects unused declared parameters,
        // which corrupts binding for all subsequent parameters (@pendingStatus, etc.).
        var sqlParams = new List<SqlParameter>
        {
            new SqlParameter("@batchSize",              SqlDbType.Int)      { Value = batchSize },
            new SqlParameter("@processingStatus",       SqlDbType.Int)      { Value = (int)MessageStatus.Processing },
            new SqlParameter("@pendingStatus",          SqlDbType.Int)      { Value = (int)MessageStatus.Pending },
            new SqlParameter("@visibilityTimeoutSeconds", SqlDbType.Int)    { Value = visibilityTimeoutSeconds },
            new SqlParameter("@lockedBy",               SqlDbType.NVarChar, 256) { Value = lockedBy },
        };

        if (_options.TenantFilter is not null)
            sqlParams.Add(new SqlParameter("@tenantFilter", SqlDbType.NVarChar, 256) { Value = _options.TenantFilter });

        if (skipIds is { Count: > 0 })
            sqlParams.Add(new SqlParameter("@skipJson", SqlDbType.NVarChar, -1)
                { Value = System.Text.Json.JsonSerializer.Serialize(skipIds) });

        var messages = await _dbContext.OutboxMessages
            .FromSqlRaw(sql, sqlParams.ToArray<object>())
            .AsNoTracking()
            .ToListAsync(ct);

        _logger.LogDebug(
            "Locked {Count} outbox messages for processing by {LockedBy}",
            messages.Count,
            lockedBy);

        return messages;
    }

    public async Task<OutboxMessage?> TryLockByIdAsync(
        Guid messageId,
        TimeSpan visibilityTimeout,
        string lockedBy,
        CancellationToken ct = default)
    {
        var schema = _options.SchemaName;
        var timeoutSeconds = (int)visibilityTimeout.TotalSeconds;

        // PK-seek UPDATE — no table scan. SQL Server blocks on an uncommitted INSERT
        // for this ID (read-committed), so the signal racing ahead of the commit is safe:
        // the UPDATE succeeds when the outer transaction commits, or returns 0 rows if
        // it rolls back.
        var sql = $"""
            UPDATE [{schema}].[OutboxMessages]
            SET [Status]      = @processingStatus,
                [LockedUntil] = DATEADD(SECOND, @timeoutSeconds, SYSDATETIMEOFFSET()),
                [LockedBy]    = @lockedBy
            OUTPUT
                INSERTED.[Id], INSERTED.[EventType], INSERTED.[Payload],
                INSERTED.[CorrelationId], INSERTED.[TraceId], INSERTED.[Status],
                INSERTED.[RetryCount], INSERTED.[CreatedAt], INSERTED.[ProcessedAt],
                INSERTED.[LockedUntil], INSERTED.[LockedBy], INSERTED.[NextRetryAt],
                INSERTED.[LastError], INSERTED.[Headers],
                INSERTED.[TenantId], INSERTED.[UserId], INSERTED.[EntityId]
            WHERE [Id]     = @id
              AND [Status] = @pendingStatus
              AND ([LockedUntil] IS NULL OR [LockedUntil] < SYSDATETIMEOFFSET())
              AND ([NextRetryAt] IS NULL OR [NextRetryAt] <= SYSDATETIMEOFFSET())
            """;

        var results = await _dbContext.OutboxMessages
            .FromSqlRaw(sql,
                new SqlParameter("@id",               SqlDbType.UniqueIdentifier) { Value = messageId },
                new SqlParameter("@processingStatus", SqlDbType.Int)              { Value = (int)MessageStatus.Processing },
                new SqlParameter("@pendingStatus",    SqlDbType.Int)              { Value = (int)MessageStatus.Pending },
                new SqlParameter("@timeoutSeconds",   SqlDbType.Int)              { Value = timeoutSeconds },
                new SqlParameter("@lockedBy",         SqlDbType.NVarChar, 256)   { Value = lockedBy })
            .AsNoTracking()
            .ToListAsync(ct);

        return results.Count > 0 ? results[0] : null;
    }

    public async Task<bool> MarkAsProcessedAsync(Guid messageId, string lockedBy, CancellationToken ct = default)
    {
        var affected = await _dbContext.OutboxMessages
            .Where(m => m.Id == messageId && m.LockedBy == lockedBy)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, MessageStatus.Delivered)
                .SetProperty(m => m.ProcessedAt, DateTimeOffset.UtcNow)
                .SetProperty(m => m.LockedUntil, (DateTimeOffset?)null)
                .SetProperty(m => m.LockedBy, (string?)null), ct);

        return affected > 0;
    }

    public async Task<bool> MarkAsFailedAsync(Guid messageId, string lockedBy, string error, CancellationToken ct = default)
    {
        var affected = await _dbContext.OutboxMessages
            .Where(m => m.Id == messageId && m.LockedBy == lockedBy)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, MessageStatus.Failed)
                .SetProperty(m => m.LastError, error)
                .SetProperty(m => m.LockedUntil, (DateTimeOffset?)null)
                .SetProperty(m => m.LockedBy, (string?)null), ct);

        return affected > 0;
    }

    public async Task<bool> IncrementRetryAsync(Guid messageId, string lockedBy, DateTimeOffset nextRetryAt, string? error = null, CancellationToken ct = default)
    {
        var affected = await _dbContext.OutboxMessages
            .Where(m => m.Id == messageId && m.LockedBy == lockedBy)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, MessageStatus.Pending)
                .SetProperty(m => m.RetryCount, m => m.RetryCount + 1)
                .SetProperty(m => m.NextRetryAt, nextRetryAt)
                .SetProperty(m => m.LastError, error)
                .SetProperty(m => m.LockedUntil, (DateTimeOffset?)null)
                .SetProperty(m => m.LockedBy, (string?)null), ct);

        return affected > 0;
    }

    public async Task<bool> MarkAsDeadLetteredAsync(Guid messageId, string lockedBy, CancellationToken ct = default)
    {
        var affected = await _dbContext.OutboxMessages
            .Where(m => m.Id == messageId && m.LockedBy == lockedBy)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, MessageStatus.DeadLettered)
                .SetProperty(m => m.LockedUntil, (DateTimeOffset?)null)
                .SetProperty(m => m.LockedBy, (string?)null), ct);

        return affected > 0;
    }

    public async Task<bool> IsLockHeldAsync(Guid messageId, string lockedBy, CancellationToken ct = default)
    {
        return await _dbContext.OutboxMessages
            .AnyAsync(m => m.Id == messageId
                        && m.LockedBy == lockedBy
                        && m.Status == MessageStatus.Processing
                        && m.LockedUntil != null
                        && m.LockedUntil > DateTimeOffset.UtcNow, ct);
    }

    public async Task ReleaseExpiredLocksAsync(CancellationToken ct = default)
    {
        // Do NOT increment RetryCount here. An expired lock means the processor
        // crashed or was killed — it is an infrastructure failure, not a delivery
        // failure. Counting it against the message's retry budget would dead-letter
        // messages prematurely under transient processor outages.
        var released = await _dbContext.OutboxMessages
            .Where(m => m.Status == MessageStatus.Processing
                     && m.LockedUntil != null
                     && m.LockedUntil < DateTimeOffset.UtcNow)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, MessageStatus.Pending)
                .SetProperty(m => m.LockedUntil, (DateTimeOffset?)null)
                .SetProperty(m => m.LockedBy, (string?)null), ct);

        if (released > 0)
            _logger.LogWarning("Released {Count} expired message locks", released);
    }

    public async Task<int> PurgeProcessedMessagesAsync(DateTimeOffset olderThan, CancellationToken ct = default)
    {
        var deleted = await _dbContext.OutboxMessages
            .Where(m => (m.Status == MessageStatus.Delivered || m.Status == MessageStatus.DeadLettered)
                     && m.CreatedAt < olderThan)
            .ExecuteDeleteAsync(ct);

        if (deleted > 0)
            _logger.LogInformation("Purged {Count} processed/dead-lettered outbox messages older than {OlderThan}", deleted, olderThan);

        return deleted;
    }
}
