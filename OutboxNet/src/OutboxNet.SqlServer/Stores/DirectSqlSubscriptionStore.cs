using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using OutboxNet.Interfaces;
using OutboxNet.Models;
using OutboxNet.Options;

namespace OutboxNet.SqlServer.Stores;

internal sealed class DirectSqlSubscriptionStore : ISubscriptionStore
{
    private readonly string _connectionString;
    private readonly string _schema;
    private readonly ITenantSecretRetriever? _secretRetriever;

    public DirectSqlSubscriptionStore(
        IOptions<DirectSqlOptions> directSqlOptions,
        IOptions<OutboxOptions> options,
        ITenantSecretRetriever? secretRetriever = null)
    {
        _connectionString = directSqlOptions.Value.ConnectionString;
        _schema = options.Value.SchemaName;
        _secretRetriever = secretRetriever;
    }

    public async Task<WebhookSubscription> AddAsync(WebhookSubscription subscription, CancellationToken ct = default)
    {
        subscription.CreatedAt = DateTimeOffset.UtcNow;

        if (subscription.Id == Guid.Empty)
            subscription.Id = Guid.NewGuid();

        var sql = $"""
            INSERT INTO [{_schema}].[WebhookSubscriptions]
                ([Id], [TenantId], [EventType], [WebhookUrl], [Secret], [IsActive], [MaxRetries], [TimeoutSeconds], [CreatedAt], [UpdatedAt], [CustomHeaders])
            VALUES
                (@Id, @TenantId, @EventType, @WebhookUrl, @Secret, @IsActive, @MaxRetries, @TimeoutSeconds, @CreatedAt, @UpdatedAt, @CustomHeaders)
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new SqlParameter("@Id", SqlDbType.UniqueIdentifier) { Value = subscription.Id });
        command.Parameters.Add(new SqlParameter("@TenantId", SqlDbType.NVarChar, 256) { Value = (object?)subscription.TenantId ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@EventType", SqlDbType.NVarChar, 256) { Value = subscription.EventType });
        command.Parameters.Add(new SqlParameter("@WebhookUrl", SqlDbType.NVarChar, 2048) { Value = subscription.WebhookUrl });
        command.Parameters.Add(new SqlParameter("@Secret", SqlDbType.NVarChar, 512) { Value = subscription.Secret });
        command.Parameters.Add(new SqlParameter("@IsActive", SqlDbType.Bit) { Value = subscription.IsActive });
        command.Parameters.Add(new SqlParameter("@MaxRetries", SqlDbType.Int) { Value = subscription.MaxRetries });
        command.Parameters.Add(new SqlParameter("@TimeoutSeconds", SqlDbType.Int) { Value = (int)subscription.Timeout.TotalSeconds });
        command.Parameters.Add(new SqlParameter("@CreatedAt", SqlDbType.DateTimeOffset) { Value = subscription.CreatedAt });
        command.Parameters.Add(new SqlParameter("@UpdatedAt", SqlDbType.DateTimeOffset) { Value = (object?)subscription.UpdatedAt ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@CustomHeaders", SqlDbType.NVarChar, -1)
        {
            Value = subscription.CustomHeaders != null
                ? (object)JsonSerializer.Serialize(subscription.CustomHeaders)
                : DBNull.Value
        });

        await command.ExecuteNonQueryAsync(ct);
        return subscription;
    }

    public async Task<WebhookSubscription?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var sql = $"""
            SELECT [Id], [TenantId], [EventType], [WebhookUrl], [Secret], [IsActive], [MaxRetries], [TimeoutSeconds], [CreatedAt], [UpdatedAt], [CustomHeaders]
            FROM [{_schema}].[WebhookSubscriptions]
            WHERE [Id] = @Id
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new SqlParameter("@Id", SqlDbType.UniqueIdentifier) { Value = id });

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
            return OutboxMessageMapper.MapSubscriptionFromReader(reader);

        return null;
    }

    public async Task<IReadOnlyList<WebhookSubscription>> GetByEventTypeAsync(string eventType, CancellationToken ct = default)
    {
        var sql = $"""
            SELECT [Id], [TenantId], [EventType], [WebhookUrl], [Secret], [IsActive], [MaxRetries], [TimeoutSeconds], [CreatedAt], [UpdatedAt], [CustomHeaders]
            FROM [{_schema}].[WebhookSubscriptions]
            WHERE [IsActive] = 1 AND ([EventType] = @EventType OR [EventType] = '*')
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new SqlParameter("@EventType", SqlDbType.NVarChar, 256) { Value = eventType });

        var subscriptions = new List<WebhookSubscription>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            subscriptions.Add(OutboxMessageMapper.MapSubscriptionFromReader(reader));

        return subscriptions;
    }

    public async Task<IReadOnlyList<WebhookSubscription>> GetForMessageAsync(OutboxMessage message, CancellationToken ct = default)
    {
        // Global subscriptions (TenantId IS NULL) always match.
        // Tenant-specific subscriptions match only when TenantId equals the message's TenantId.
        var sql = $"""
            SELECT [Id], [TenantId], [EventType], [WebhookUrl], [Secret], [IsActive], [MaxRetries], [TimeoutSeconds], [CreatedAt], [UpdatedAt], [CustomHeaders]
            FROM [{_schema}].[WebhookSubscriptions]
            WHERE [IsActive] = 1
              AND ([EventType] = @EventType OR [EventType] = '*')
              AND ([TenantId] IS NULL OR [TenantId] = @TenantId)
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new SqlParameter("@EventType", SqlDbType.NVarChar, 256) { Value = message.EventType });
        command.Parameters.Add(new SqlParameter("@TenantId", SqlDbType.NVarChar, 256) { Value = (object?)message.TenantId ?? DBNull.Value });

        var subscriptions = new List<WebhookSubscription>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            subscriptions.Add(OutboxMessageMapper.MapSubscriptionFromReader(reader));

        return await EnrichSecretsAsync(subscriptions, ct);
    }

    public async Task UpdateAsync(WebhookSubscription subscription, CancellationToken ct = default)
    {
        subscription.UpdatedAt = DateTimeOffset.UtcNow;

        var sql = $"""
            UPDATE [{_schema}].[WebhookSubscriptions]
            SET [TenantId] = @TenantId,
                [EventType] = @EventType,
                [WebhookUrl] = @WebhookUrl,
                [Secret] = @Secret,
                [IsActive] = @IsActive,
                [MaxRetries] = @MaxRetries,
                [TimeoutSeconds] = @TimeoutSeconds,
                [UpdatedAt] = @UpdatedAt,
                [CustomHeaders] = @CustomHeaders
            WHERE [Id] = @Id
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new SqlParameter("@Id", SqlDbType.UniqueIdentifier) { Value = subscription.Id });
        command.Parameters.Add(new SqlParameter("@TenantId", SqlDbType.NVarChar, 256) { Value = (object?)subscription.TenantId ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@EventType", SqlDbType.NVarChar, 256) { Value = subscription.EventType });
        command.Parameters.Add(new SqlParameter("@WebhookUrl", SqlDbType.NVarChar, 2048) { Value = subscription.WebhookUrl });
        command.Parameters.Add(new SqlParameter("@Secret", SqlDbType.NVarChar, 512) { Value = subscription.Secret });
        command.Parameters.Add(new SqlParameter("@IsActive", SqlDbType.Bit) { Value = subscription.IsActive });
        command.Parameters.Add(new SqlParameter("@MaxRetries", SqlDbType.Int) { Value = subscription.MaxRetries });
        command.Parameters.Add(new SqlParameter("@TimeoutSeconds", SqlDbType.Int) { Value = (int)subscription.Timeout.TotalSeconds });
        command.Parameters.Add(new SqlParameter("@UpdatedAt", SqlDbType.DateTimeOffset) { Value = subscription.UpdatedAt });
        command.Parameters.Add(new SqlParameter("@CustomHeaders", SqlDbType.NVarChar, -1)
        {
            Value = subscription.CustomHeaders != null
                ? (object)JsonSerializer.Serialize(subscription.CustomHeaders)
                : DBNull.Value
        });

        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken ct = default)
    {
        var sql = $"""
            UPDATE [{_schema}].[WebhookSubscriptions]
            SET [IsActive] = 0,
                [UpdatedAt] = SYSDATETIMEOFFSET()
            WHERE [Id] = @Id
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new SqlParameter("@Id", SqlDbType.UniqueIdentifier) { Value = id });

        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task<IReadOnlyList<WebhookSubscription>> EnrichSecretsAsync(
        List<WebhookSubscription> subscriptions,
        CancellationToken ct)
    {
        if (_secretRetriever is null || subscriptions.Count == 0)
            return subscriptions;

        foreach (var sub in subscriptions)
        {
            var tenantKey = sub.TenantId ?? sub.Id.ToString();
            var secret = await _secretRetriever.GetSecretAsync(tenantKey, ct);
            if (secret is not null)
                sub.Secret = secret;
        }

        return subscriptions;
    }
}
