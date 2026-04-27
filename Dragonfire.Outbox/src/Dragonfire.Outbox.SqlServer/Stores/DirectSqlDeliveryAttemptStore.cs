using System.Data;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Dragonfire.Outbox.Interfaces;
using Dragonfire.Outbox.Models;
using Dragonfire.Outbox.Options;

namespace Dragonfire.Outbox.SqlServer.Stores;

internal sealed class DirectSqlDeliveryAttemptStore : IDeliveryAttemptStore
{
    private readonly string _connectionString;
    private readonly string _schema;

    public DirectSqlDeliveryAttemptStore(
        IOptions<DirectSqlOptions> directSqlOptions,
        IOptions<OutboxOptions> options)
    {
        _connectionString = directSqlOptions.Value.ConnectionString;
        _schema = options.Value.SchemaName;
    }

    public async Task SaveAttemptAsync(DeliveryAttempt attempt, CancellationToken ct = default)
    {
        if (attempt.Id == Guid.Empty)
            attempt.Id = Guid.NewGuid();

        var sql = $"""
            INSERT INTO [{_schema}].[DeliveryAttempts]
                ([Id], [OutboxMessageId], [WebhookSubscriptionId], [AttemptNumber], [Status], [HttpStatusCode], [ResponseBody], [ErrorMessage], [DurationMs], [AttemptedAt], [NextRetryAt])
            VALUES
                (@Id, @OutboxMessageId, @WebhookSubscriptionId, @AttemptNumber, @Status, @HttpStatusCode, @ResponseBody, @ErrorMessage, @DurationMs, @AttemptedAt, @NextRetryAt)
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = BuildInsertCommand(connection, attempt, sql);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Inserts all attempts in a single connection using a VALUES row-list — one round-trip
    /// regardless of how many subscriptions were attempted.
    /// </summary>
    public async Task SaveAttemptsAsync(IReadOnlyList<DeliveryAttempt> attempts, CancellationToken ct = default)
    {
        if (attempts.Count == 0) return;
        if (attempts.Count == 1) { await SaveAttemptAsync(attempts[0], ct); return; }

        // Build parameterised multi-row INSERT:
        // INSERT INTO ... VALUES (@Id0,@MsgId0,...), (@Id1,@MsgId1,...), ...
        var sqlBuilder = new StringBuilder();
        sqlBuilder.Append($"INSERT INTO [{_schema}].[DeliveryAttempts] ([Id],[OutboxMessageId],[WebhookSubscriptionId],[AttemptNumber],[Status],[HttpStatusCode],[ResponseBody],[ErrorMessage],[DurationMs],[AttemptedAt],[NextRetryAt]) VALUES ");

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();

        for (var i = 0; i < attempts.Count; i++)
        {
            var a = attempts[i];
            if (a.Id == Guid.Empty) a.Id = Guid.NewGuid();

            if (i > 0) sqlBuilder.Append(',');
            sqlBuilder.Append($"(@Id{i},@MsgId{i},@SubId{i},@Att{i},@St{i},@Http{i},@Resp{i},@Err{i},@Dur{i},@At{i},@Next{i})");

            command.Parameters.Add(new SqlParameter($"@Id{i}",   SqlDbType.UniqueIdentifier) { Value = a.Id });
            command.Parameters.Add(new SqlParameter($"@MsgId{i}",SqlDbType.UniqueIdentifier) { Value = a.OutboxMessageId });
            command.Parameters.Add(new SqlParameter($"@SubId{i}",SqlDbType.UniqueIdentifier) { Value = a.WebhookSubscriptionId });
            command.Parameters.Add(new SqlParameter($"@Att{i}",  SqlDbType.Int)              { Value = a.AttemptNumber });
            command.Parameters.Add(new SqlParameter($"@St{i}",   SqlDbType.Int)              { Value = (int)a.Status });
            command.Parameters.Add(new SqlParameter($"@Http{i}", SqlDbType.Int)              { Value = (object?)a.HttpStatusCode ?? DBNull.Value });
            command.Parameters.Add(new SqlParameter($"@Resp{i}", SqlDbType.NVarChar, 4000)   { Value = (object?)a.ResponseBody   ?? DBNull.Value });
            command.Parameters.Add(new SqlParameter($"@Err{i}",  SqlDbType.NVarChar, -1)     { Value = (object?)a.ErrorMessage   ?? DBNull.Value });
            command.Parameters.Add(new SqlParameter($"@Dur{i}",  SqlDbType.BigInt)           { Value = a.DurationMs });
            command.Parameters.Add(new SqlParameter($"@At{i}",   SqlDbType.DateTimeOffset)   { Value = a.AttemptedAt == default ? DateTimeOffset.UtcNow : a.AttemptedAt });
            command.Parameters.Add(new SqlParameter($"@Next{i}", SqlDbType.DateTimeOffset)   { Value = (object?)a.NextRetryAt ?? DBNull.Value });
        }

        command.CommandText = sqlBuilder.ToString();
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<DeliveryAttempt>> GetByMessageIdAsync(Guid messageId, CancellationToken ct = default)
    {
        var sql = $"""
            SELECT [Id], [OutboxMessageId], [WebhookSubscriptionId], [AttemptNumber], [Status], [HttpStatusCode], [ResponseBody], [ErrorMessage], [DurationMs], [AttemptedAt], [NextRetryAt]
            FROM [{_schema}].[DeliveryAttempts]
            WHERE [OutboxMessageId] = @OutboxMessageId
            ORDER BY [AttemptNumber]
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new SqlParameter("@OutboxMessageId", SqlDbType.UniqueIdentifier) { Value = messageId });

        var attempts = new List<DeliveryAttempt>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            attempts.Add(OutboxMessageMapper.MapDeliveryAttemptFromReader(reader));

        return attempts;
    }

    public async Task<IReadOnlyList<DeliveryAttempt>> GetBySubscriptionIdAsync(
        Guid subscriptionId,
        int limit = 50,
        CancellationToken ct = default)
    {
        var sql = $"""
            SELECT TOP (@Limit) [Id], [OutboxMessageId], [WebhookSubscriptionId], [AttemptNumber], [Status], [HttpStatusCode], [ResponseBody], [ErrorMessage], [DurationMs], [AttemptedAt], [NextRetryAt]
            FROM [{_schema}].[DeliveryAttempts]
            WHERE [WebhookSubscriptionId] = @WebhookSubscriptionId
            ORDER BY [AttemptedAt] DESC
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new SqlParameter("@WebhookSubscriptionId", SqlDbType.UniqueIdentifier) { Value = subscriptionId });
        command.Parameters.Add(new SqlParameter("@Limit", SqlDbType.Int) { Value = limit });

        var attempts = new List<DeliveryAttempt>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            attempts.Add(OutboxMessageMapper.MapDeliveryAttemptFromReader(reader));

        return attempts;
    }

    public async Task<IReadOnlyDictionary<Guid, SubscriptionDeliveryState>> GetDeliveryStatesAsync(
        Guid messageId,
        IReadOnlyList<Guid> subscriptionIds,
        CancellationToken ct = default)
    {
        if (subscriptionIds.Count == 0)
            return new Dictionary<Guid, SubscriptionDeliveryState>();

        // Use OPENJSON to pass the subscription ID list as a single JSON parameter
        // instead of a variable-length parameterised IN-list. Benefits:
        //  • Single stable query plan regardless of subscription count.
        //  • No per-call plan-cache pollution.
        //  • Safe against SQL injection by design.
        var sql = $"""
            SELECT d.[WebhookSubscriptionId],
                   COUNT(*)                                                       AS AttemptCount,
                   MAX(CASE WHEN d.[Status] = @SuccessStatus THEN 1 ELSE 0 END)  AS HasSuccess
            FROM [{_schema}].[DeliveryAttempts] d
            INNER JOIN OPENJSON(@Ids) WITH ([value] UNIQUEIDENTIFIER '$') AS j
                    ON d.[WebhookSubscriptionId] = j.[value]
            WHERE d.[OutboxMessageId] = @OutboxMessageId
            GROUP BY d.[WebhookSubscriptionId]
            """;

        var idsJson = System.Text.Json.JsonSerializer.Serialize(subscriptionIds);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new SqlParameter("@OutboxMessageId", SqlDbType.UniqueIdentifier) { Value = messageId });
        command.Parameters.Add(new SqlParameter("@SuccessStatus",   SqlDbType.Int)              { Value = (int)DeliveryStatus.Success });
        command.Parameters.Add(new SqlParameter("@Ids",             SqlDbType.NVarChar, -1)     { Value = idsJson });

        var result = new Dictionary<Guid, SubscriptionDeliveryState>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var subId      = reader.GetGuid(0);
            var count      = reader.GetInt32(1);
            var hasSuccess = reader.GetInt32(2) == 1;
            result[subId]  = new SubscriptionDeliveryState(count, hasSuccess);
        }

        return result;
    }

    public async Task<int> PurgeOldAttemptsAsync(DateTimeOffset olderThan, CancellationToken ct = default)
    {
        var sql = $"""
            DELETE FROM [{_schema}].[DeliveryAttempts]
            WHERE [AttemptedAt] < @OlderThan
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new SqlParameter("@OlderThan", SqlDbType.DateTimeOffset) { Value = olderThan });

        return await command.ExecuteNonQueryAsync(ct);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static SqlCommand BuildInsertCommand(SqlConnection connection, DeliveryAttempt a, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new SqlParameter("@Id",                   SqlDbType.UniqueIdentifier) { Value = a.Id });
        command.Parameters.Add(new SqlParameter("@OutboxMessageId",      SqlDbType.UniqueIdentifier) { Value = a.OutboxMessageId });
        command.Parameters.Add(new SqlParameter("@WebhookSubscriptionId",SqlDbType.UniqueIdentifier) { Value = a.WebhookSubscriptionId });
        command.Parameters.Add(new SqlParameter("@AttemptNumber",        SqlDbType.Int)              { Value = a.AttemptNumber });
        command.Parameters.Add(new SqlParameter("@Status",               SqlDbType.Int)              { Value = (int)a.Status });
        command.Parameters.Add(new SqlParameter("@HttpStatusCode",        SqlDbType.Int)              { Value = (object?)a.HttpStatusCode ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@ResponseBody",          SqlDbType.NVarChar, 4000)   { Value = (object?)a.ResponseBody   ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@ErrorMessage",          SqlDbType.NVarChar, -1)     { Value = (object?)a.ErrorMessage   ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@DurationMs",            SqlDbType.BigInt)           { Value = a.DurationMs });
        command.Parameters.Add(new SqlParameter("@AttemptedAt",           SqlDbType.DateTimeOffset)   { Value = a.AttemptedAt == default ? DateTimeOffset.UtcNow : a.AttemptedAt });
        command.Parameters.Add(new SqlParameter("@NextRetryAt",           SqlDbType.DateTimeOffset)   { Value = (object?)a.NextRetryAt ?? DBNull.Value });
        return command;
    }
}
