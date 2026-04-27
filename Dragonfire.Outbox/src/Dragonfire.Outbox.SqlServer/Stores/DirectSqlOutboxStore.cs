using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Dragonfire.Outbox.Interfaces;
using Dragonfire.Outbox.Models;
using Dragonfire.Outbox.Options;

namespace Dragonfire.Outbox.SqlServer.Stores;

internal sealed class DirectSqlOutboxStore : IOutboxStore
{
    private readonly string _connectionString;
    private readonly OutboxOptions _options;
    private readonly ILogger<DirectSqlOutboxStore> _logger;

    public DirectSqlOutboxStore(
        IOptions<DirectSqlOptions> directSqlOptions,
        IOptions<OutboxOptions> options,
        ILogger<DirectSqlOutboxStore> logger)
    {
        _connectionString = directSqlOptions.Value.ConnectionString;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SaveMessageAsync(OutboxMessage message, CancellationToken ct = default)
    {
        var schema = _options.SchemaName;
        var sql = $"""
            INSERT INTO [{schema}].[OutboxMessages]
                ([Id], [EventType], [Payload], [CorrelationId], [TraceId], [Status], [RetryCount], [CreatedAt], [Headers], [TenantId], [UserId], [EntityId])
            VALUES
                (@Id, @EventType, @Payload, @CorrelationId, @TraceId, @Status, @RetryCount, @CreatedAt, @Headers, @TenantId, @UserId, @EntityId)
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new SqlParameter("@Id", SqlDbType.UniqueIdentifier) { Value = message.Id == Guid.Empty ? Guid.NewGuid() : message.Id });
        command.Parameters.Add(new SqlParameter("@EventType", SqlDbType.NVarChar, 256) { Value = message.EventType });
        command.Parameters.Add(new SqlParameter("@Payload", SqlDbType.NVarChar, -1) { Value = message.Payload });
        command.Parameters.Add(new SqlParameter("@CorrelationId", SqlDbType.NVarChar, 128) { Value = (object?)message.CorrelationId ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@TraceId", SqlDbType.NVarChar, 128) { Value = (object?)message.TraceId ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@Status", SqlDbType.Int) { Value = (int)message.Status });
        command.Parameters.Add(new SqlParameter("@RetryCount", SqlDbType.Int) { Value = message.RetryCount });
        command.Parameters.Add(new SqlParameter("@CreatedAt", SqlDbType.DateTimeOffset) { Value = message.CreatedAt == default ? DateTimeOffset.UtcNow : message.CreatedAt });
        command.Parameters.Add(new SqlParameter("@Headers", SqlDbType.NVarChar, -1)
        {
            Value = message.Headers != null
                ? (object)System.Text.Json.JsonSerializer.Serialize(message.Headers)
                : DBNull.Value
        });
        command.Parameters.Add(new SqlParameter("@TenantId", SqlDbType.NVarChar, 256) { Value = (object?)message.TenantId ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@UserId", SqlDbType.NVarChar, 256) { Value = (object?)message.UserId ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@EntityId", SqlDbType.NVarChar, 256) { Value = (object?)message.EntityId ?? DBNull.Value });

        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<OutboxMessage>> LockNextBatchAsync(
        int batchSize,
        TimeSpan visibilityTimeout,
        string lockedBy,
        IReadOnlySet<Guid>? skipIds = null,
        CancellationToken ct = default)
    {
        var schema = _options.SchemaName;

        var tenantFilterClause = _options.TenantFilter is not null
            ? "AND m.[TenantId] = @TenantFilter"
            : string.Empty;

        var skipIdsClause = skipIds is { Count: > 0 }
            ? "AND m.[Id] NOT IN (SELECT CAST([value] AS uniqueidentifier) FROM OPENJSON(@SkipJson))"
            : string.Empty;

        // Null-safe partition equality: (a = b) OR (a IS NULL AND b IS NULL)
        var orderingClause = _options.EnableOrderedProcessing
            ? $"""

              AND (
                (m.[TenantId] IS NULL AND m.[UserId] IS NULL AND m.[EntityId] IS NULL)
                OR NOT EXISTS (
                    SELECT 1 FROM [{schema}].[OutboxMessages] m2 WITH (READCOMMITTEDLOCK)
                    WHERE m2.[Status] = @ProcessingStatus
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
                SELECT TOP (@BatchSize) m.[Id]
                FROM [{schema}].[OutboxMessages] m WITH (UPDLOCK, READPAST)
                WHERE m.[Status] IN (@PendingStatus, @ProcessingStatus)
                  AND (m.[LockedUntil] IS NULL OR m.[LockedUntil] < SYSDATETIMEOFFSET())
                  AND (m.[NextRetryAt] IS NULL OR m.[NextRetryAt] <= SYSDATETIMEOFFSET())
                  {tenantFilterClause}
                  {skipIdsClause}
                {orderingClause}
                ORDER BY m.[CreatedAt]
            )
            UPDATE m
            SET m.[Status] = @ProcessingStatus,
                m.[LockedUntil] = DATEADD(SECOND, @VisibilityTimeoutSeconds, SYSDATETIMEOFFSET()),
                m.[LockedBy] = @LockedBy
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

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new SqlParameter("@BatchSize", SqlDbType.Int) { Value = batchSize });
        command.Parameters.Add(new SqlParameter("@ProcessingStatus", SqlDbType.Int) { Value = (int)MessageStatus.Processing });
        command.Parameters.Add(new SqlParameter("@PendingStatus", SqlDbType.Int) { Value = (int)MessageStatus.Pending });
        command.Parameters.Add(new SqlParameter("@VisibilityTimeoutSeconds", SqlDbType.Int) { Value = (int)visibilityTimeout.TotalSeconds });
        command.Parameters.Add(new SqlParameter("@LockedBy", SqlDbType.NVarChar, 256) { Value = lockedBy });
        // Only declare @TenantFilter / @SkipJson when the clauses are actually in the SQL.
        // sp_executesql rejects declared parameters that are not referenced in the query body.
        if (_options.TenantFilter is not null)
            command.Parameters.Add(new SqlParameter("@TenantFilter", SqlDbType.NVarChar, 256) { Value = _options.TenantFilter });
        if (skipIds is { Count: > 0 })
            command.Parameters.Add(new SqlParameter("@SkipJson", SqlDbType.NVarChar, -1)
                { Value = System.Text.Json.JsonSerializer.Serialize(skipIds) });

        var messages = new List<OutboxMessage>();

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            messages.Add(OutboxMessageMapper.MapFromReader(reader));
        }

        _logger.LogDebug("Locked {Count} outbox messages for processing by {LockedBy}", messages.Count, lockedBy);
        return messages;
    }

    public async Task<bool> MarkAsProcessedAsync(Guid messageId, string lockedBy, CancellationToken ct = default)
    {
        var schema = _options.SchemaName;
        var sql = $"""
            UPDATE [{schema}].[OutboxMessages]
            SET [Status] = @Status,
                [ProcessedAt] = SYSDATETIMEOFFSET(),
                [LockedUntil] = NULL,
                [LockedBy] = NULL
            WHERE [Id] = @Id AND [LockedBy] = @LockedBy
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new SqlParameter("@Status", SqlDbType.Int) { Value = (int)MessageStatus.Delivered });
        command.Parameters.Add(new SqlParameter("@Id", SqlDbType.UniqueIdentifier) { Value = messageId });
        command.Parameters.Add(new SqlParameter("@LockedBy", SqlDbType.NVarChar, 256) { Value = lockedBy });

        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> MarkAsFailedAsync(Guid messageId, string lockedBy, string error, CancellationToken ct = default)
    {
        var schema = _options.SchemaName;
        var sql = $"""
            UPDATE [{schema}].[OutboxMessages]
            SET [Status] = @Status,
                [LastError] = @LastError,
                [LockedUntil] = NULL,
                [LockedBy] = NULL
            WHERE [Id] = @Id AND [LockedBy] = @LockedBy
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new SqlParameter("@Status", SqlDbType.Int) { Value = (int)MessageStatus.Failed });
        command.Parameters.Add(new SqlParameter("@LastError", SqlDbType.NVarChar, -1) { Value = (object?)error ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@Id", SqlDbType.UniqueIdentifier) { Value = messageId });
        command.Parameters.Add(new SqlParameter("@LockedBy", SqlDbType.NVarChar, 256) { Value = lockedBy });

        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> IncrementRetryAsync(Guid messageId, string lockedBy, DateTimeOffset nextRetryAt, string? error = null, CancellationToken ct = default)
    {
        var schema = _options.SchemaName;
        var sql = $"""
            UPDATE [{schema}].[OutboxMessages]
            SET [Status] = @Status,
                [RetryCount] = [RetryCount] + 1,
                [NextRetryAt] = @NextRetryAt,
                [LastError] = @LastError,
                [LockedUntil] = NULL,
                [LockedBy] = NULL
            WHERE [Id] = @Id AND [LockedBy] = @LockedBy
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new SqlParameter("@Status", SqlDbType.Int) { Value = (int)MessageStatus.Pending });
        command.Parameters.Add(new SqlParameter("@NextRetryAt", SqlDbType.DateTimeOffset) { Value = nextRetryAt });
        command.Parameters.Add(new SqlParameter("@LastError", SqlDbType.NVarChar, -1) { Value = (object?)error ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@Id", SqlDbType.UniqueIdentifier) { Value = messageId });
        command.Parameters.Add(new SqlParameter("@LockedBy", SqlDbType.NVarChar, 256) { Value = lockedBy });

        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> MarkAsDeadLetteredAsync(Guid messageId, string lockedBy, CancellationToken ct = default)
    {
        var schema = _options.SchemaName;
        var sql = $"""
            UPDATE [{schema}].[OutboxMessages]
            SET [Status] = @Status,
                [LockedUntil] = NULL,
                [LockedBy] = NULL
            WHERE [Id] = @Id AND [LockedBy] = @LockedBy
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new SqlParameter("@Status", SqlDbType.Int) { Value = (int)MessageStatus.DeadLettered });
        command.Parameters.Add(new SqlParameter("@Id", SqlDbType.UniqueIdentifier) { Value = messageId });
        command.Parameters.Add(new SqlParameter("@LockedBy", SqlDbType.NVarChar, 256) { Value = lockedBy });

        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<OutboxMessage?> TryLockByIdAsync(
        Guid messageId,
        TimeSpan visibilityTimeout,
        string lockedBy,
        CancellationToken ct = default)
    {
        var schema = _options.SchemaName;
        var timeoutSeconds = (int)visibilityTimeout.TotalSeconds;

        var sql = $"""
            UPDATE [{schema}].[OutboxMessages]
            SET [Status]      = @ProcessingStatus,
                [LockedUntil] = DATEADD(SECOND, @TimeoutSeconds, SYSDATETIMEOFFSET()),
                [LockedBy]    = @LockedBy
            OUTPUT
                INSERTED.[Id], INSERTED.[EventType], INSERTED.[Payload],
                INSERTED.[CorrelationId], INSERTED.[TraceId], INSERTED.[Status],
                INSERTED.[RetryCount], INSERTED.[CreatedAt], INSERTED.[ProcessedAt],
                INSERTED.[LockedUntil], INSERTED.[LockedBy], INSERTED.[NextRetryAt],
                INSERTED.[LastError], INSERTED.[Headers],
                INSERTED.[TenantId], INSERTED.[UserId], INSERTED.[EntityId]
            WHERE [Id]     = @Id
              AND [Status] = @PendingStatus
              AND ([LockedUntil] IS NULL OR [LockedUntil] < SYSDATETIMEOFFSET())
              AND ([NextRetryAt] IS NULL OR [NextRetryAt] <= SYSDATETIMEOFFSET())
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new SqlParameter("@Id",               SqlDbType.UniqueIdentifier) { Value = messageId });
        command.Parameters.Add(new SqlParameter("@ProcessingStatus", SqlDbType.Int)              { Value = (int)MessageStatus.Processing });
        command.Parameters.Add(new SqlParameter("@PendingStatus",    SqlDbType.Int)              { Value = (int)MessageStatus.Pending });
        command.Parameters.Add(new SqlParameter("@TimeoutSeconds",   SqlDbType.Int)              { Value = timeoutSeconds });
        command.Parameters.Add(new SqlParameter("@LockedBy",         SqlDbType.NVarChar, 256)   { Value = lockedBy });

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
            return OutboxMessageMapper.MapFromReader(reader);

        return null;
    }

    public async Task<bool> IsLockHeldAsync(Guid messageId, string lockedBy, CancellationToken ct = default)
    {
        var schema = _options.SchemaName;
        var sql = $"""
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM [{schema}].[OutboxMessages]
                WHERE [Id] = @Id
                  AND [LockedBy] = @LockedBy
                  AND [Status] = @ProcessingStatus
                  AND [LockedUntil] IS NOT NULL
                  AND [LockedUntil] > SYSDATETIMEOFFSET()
            ) THEN 1 ELSE 0 END
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new SqlParameter("@Id", SqlDbType.UniqueIdentifier) { Value = messageId });
        command.Parameters.Add(new SqlParameter("@LockedBy", SqlDbType.NVarChar, 256) { Value = lockedBy });
        command.Parameters.Add(new SqlParameter("@ProcessingStatus", SqlDbType.Int) { Value = (int)MessageStatus.Processing });

        return (int)(await command.ExecuteScalarAsync(ct))! == 1;
    }

    public async Task ReleaseExpiredLocksAsync(CancellationToken ct = default)
    {
        var schema = _options.SchemaName;
        // Do NOT increment RetryCount here — expired lock = infrastructure failure,
        // not a delivery failure. See EfCoreOutboxStore.ReleaseExpiredLocksAsync.
        var sql = $"""
            UPDATE [{schema}].[OutboxMessages]
            SET [Status] = @PendingStatus,
                [LockedUntil] = NULL,
                [LockedBy] = NULL
            WHERE [Status] = @ProcessingStatus
              AND [LockedUntil] IS NOT NULL
              AND [LockedUntil] < SYSDATETIMEOFFSET()
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new SqlParameter("@PendingStatus", SqlDbType.Int) { Value = (int)MessageStatus.Pending });
        command.Parameters.Add(new SqlParameter("@ProcessingStatus", SqlDbType.Int) { Value = (int)MessageStatus.Processing });

        var released = await command.ExecuteNonQueryAsync(ct);

        if (released > 0)
            _logger.LogWarning("Released {Count} expired message locks", released);
    }

    public async Task<int> PurgeProcessedMessagesAsync(DateTimeOffset olderThan, CancellationToken ct = default)
    {
        var schema = _options.SchemaName;
        var sql = $"""
            DELETE FROM [{schema}].[OutboxMessages]
            WHERE [Status] IN (@DeliveredStatus, @DeadLetteredStatus)
              AND [CreatedAt] < @OlderThan
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new SqlParameter("@DeliveredStatus", SqlDbType.Int) { Value = (int)MessageStatus.Delivered });
        command.Parameters.Add(new SqlParameter("@DeadLetteredStatus", SqlDbType.Int) { Value = (int)MessageStatus.DeadLettered });
        command.Parameters.Add(new SqlParameter("@OlderThan", SqlDbType.DateTimeOffset) { Value = olderThan });

        var deleted = await command.ExecuteNonQueryAsync(ct);

        if (deleted > 0)
            _logger.LogInformation("Purged {Count} processed/dead-lettered outbox messages older than {OlderThan}", deleted, olderThan);

        return deleted;
    }
}
