using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using InboxNet.Interfaces;
using InboxNet.Models;

namespace InboxNet.EntityFrameworkCore.Stores;

internal sealed class EfCoreInboxHandlerAttemptStore : IInboxHandlerAttemptStore
{
    private readonly InboxDbContext _dbContext;

    public EfCoreInboxHandlerAttemptStore(InboxDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task SaveAttemptAsync(InboxHandlerAttempt attempt, CancellationToken ct = default)
    {
        _dbContext.InboxHandlerAttempts.Add(attempt);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task SaveAttemptsAsync(IReadOnlyList<InboxHandlerAttempt> attempts, CancellationToken ct = default)
    {
        if (attempts.Count == 0) return;
        _dbContext.InboxHandlerAttempts.AddRange(attempts);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<InboxHandlerAttempt>> GetByMessageIdAsync(Guid messageId, CancellationToken ct = default)
    {
        return await _dbContext.InboxHandlerAttempts
            .Where(a => a.InboxMessageId == messageId)
            .OrderBy(a => a.AttemptNumber)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<string, HandlerAttemptState>> GetHandlerStatesAsync(
        Guid messageId,
        IReadOnlyList<string> handlerNames,
        CancellationToken ct = default)
    {
        if (handlerNames.Count == 0)
            return new Dictionary<string, HandlerAttemptState>();

        var schema = _dbContext.Model.GetDefaultSchema() ?? "inbox";

        // OPENJSON rather than IN (@p0,@p1,...) for a stable cached plan regardless of
        // how many handlers are registered.
        var sql = $"""
            SELECT a.[HandlerName],
                   COUNT(*)                                                            AS AttemptCount,
                   MAX(CASE WHEN a.[Status] = @successStatus THEN 1 ELSE 0 END)        AS HasSuccess
            FROM [{schema}].[InboxHandlerAttempts] a
            INNER JOIN OPENJSON(@names) WITH ([value] NVARCHAR(512) '$') AS j
                    ON a.[HandlerName] = j.[value]
            WHERE a.[InboxMessageId] = @messageId
            GROUP BY a.[HandlerName]
            """;

        var namesJson = JsonSerializer.Serialize(handlerNames);

        var result = new Dictionary<string, HandlerAttemptState>(StringComparer.Ordinal);

        var conn = _dbContext.Database.GetDbConnection();
        var wasOpen = conn.State == System.Data.ConnectionState.Open;
        if (!wasOpen) await conn.OpenAsync(ct);

        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new SqlParameter("@messageId",     System.Data.SqlDbType.UniqueIdentifier) { Value = messageId });
            cmd.Parameters.Add(new SqlParameter("@successStatus", System.Data.SqlDbType.Int)              { Value = (int)InboxHandlerStatus.Success });
            cmd.Parameters.Add(new SqlParameter("@names",         System.Data.SqlDbType.NVarChar, -1)     { Value = namesJson });

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var handlerName = reader.GetString(0);
                var count       = reader.GetInt32(1);
                var hasSuccess  = reader.GetInt32(2) == 1;
                result[handlerName] = new HandlerAttemptState(count, hasSuccess);
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
        return await _dbContext.InboxHandlerAttempts
            .Where(a => a.AttemptedAt < olderThan)
            .ExecuteDeleteAsync(ct);
    }
}
