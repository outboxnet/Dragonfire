using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OutboxNet.Interfaces;
using OutboxNet.Models;

namespace OutboxNet.EntityFrameworkCore.Stores;

internal sealed class EfCoreDeliveryAttemptStore : IDeliveryAttemptStore
{
    private readonly OutboxDbContext _dbContext;

    public EfCoreDeliveryAttemptStore(OutboxDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task SaveAttemptAsync(DeliveryAttempt attempt, CancellationToken ct = default)
    {
        _dbContext.DeliveryAttempts.Add(attempt);
        await _dbContext.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Persists all attempts in a single <c>SaveChangesAsync</c> call (one round-trip).
    /// </summary>
    public async Task SaveAttemptsAsync(IReadOnlyList<DeliveryAttempt> attempts, CancellationToken ct = default)
    {
        if (attempts.Count == 0) return;
        _dbContext.DeliveryAttempts.AddRange(attempts);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<DeliveryAttempt>> GetByMessageIdAsync(Guid messageId, CancellationToken ct = default)
    {
        return await _dbContext.DeliveryAttempts
            .Where(d => d.OutboxMessageId == messageId)
            .OrderBy(d => d.AttemptNumber)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DeliveryAttempt>> GetBySubscriptionIdAsync(
        Guid subscriptionId,
        int limit = 50,
        CancellationToken ct = default)
    {
        return await _dbContext.DeliveryAttempts
            .Where(d => d.WebhookSubscriptionId == subscriptionId)
            .OrderByDescending(d => d.AttemptedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<Guid, SubscriptionDeliveryState>> GetDeliveryStatesAsync(
        Guid messageId,
        IReadOnlyList<Guid> subscriptionIds,
        CancellationToken ct = default)
    {
        if (subscriptionIds.Count == 0)
            return new Dictionary<Guid, SubscriptionDeliveryState>();

        // Use OPENJSON instead of a variable-length IN-list so SQL Server can cache a single
        // stable plan regardless of how many subscriptions a message has.
        // LINQ .Contains() generates IN (@p0,@p1,...) — a unique plan per subscription count.
        var sql = $"""
            SELECT d.[WebhookSubscriptionId],
                   COUNT(*)                                                       AS AttemptCount,
                   MAX(CASE WHEN d.[Status] = @successStatus THEN 1 ELSE 0 END)  AS HasSuccess
            FROM [{_dbContext.Model.GetDefaultSchema() ?? "outbox"}].[DeliveryAttempts] d
            INNER JOIN OPENJSON(@ids) WITH ([value] UNIQUEIDENTIFIER '$') AS j
                    ON d.[WebhookSubscriptionId] = j.[value]
            WHERE d.[OutboxMessageId] = @messageId
            GROUP BY d.[WebhookSubscriptionId]
            """;

        var idsJson = JsonSerializer.Serialize(subscriptionIds);

        var result = new Dictionary<Guid, SubscriptionDeliveryState>();

        var conn = _dbContext.Database.GetDbConnection();
        var wasOpen = conn.State == System.Data.ConnectionState.Open;
        if (!wasOpen) await conn.OpenAsync(ct);

        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new SqlParameter("@messageId",    System.Data.SqlDbType.UniqueIdentifier) { Value = messageId });
            cmd.Parameters.Add(new SqlParameter("@successStatus", System.Data.SqlDbType.Int)             { Value = (int)DeliveryStatus.Success });
            cmd.Parameters.Add(new SqlParameter("@ids",           System.Data.SqlDbType.NVarChar, -1)    { Value = idsJson });

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var subId      = reader.GetGuid(0);
                var count      = reader.GetInt32(1);
                var hasSuccess = reader.GetInt32(2) == 1;
                result[subId]  = new SubscriptionDeliveryState(count, hasSuccess);
            }
        }
        finally
        {
            if (!wasOpen) await conn.CloseAsync();
        }

        return result;
    }

    public async Task<int> PurgeOldAttemptsAsync(DateTimeOffset olderThan, CancellationToken ct = default)
    {
        return await _dbContext.DeliveryAttempts
            .Where(d => d.AttemptedAt < olderThan)
            .ExecuteDeleteAsync(ct);
    }
}
