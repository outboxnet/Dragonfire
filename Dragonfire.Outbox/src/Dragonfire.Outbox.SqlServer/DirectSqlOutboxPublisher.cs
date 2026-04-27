using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Dragonfire.Outbox.Interfaces;
using Dragonfire.Outbox.Models;
using Dragonfire.Outbox.Observability;
using Dragonfire.Outbox.Options;

namespace Dragonfire.Outbox.SqlServer;

internal sealed class DirectSqlOutboxPublisher : IOutboxPublisher
{
    private readonly ISqlTransactionAccessor _transactionAccessor;
    private readonly IMessageSerializer _serializer;
    private readonly IOutboxContextAccessor _contextAccessor;
    private readonly IOutboxSignal _signal;
    private readonly OutboxOptions _options;
    private readonly ILogger<DirectSqlOutboxPublisher> _logger;

    public DirectSqlOutboxPublisher(
        ISqlTransactionAccessor transactionAccessor,
        IMessageSerializer serializer,
        IOutboxContextAccessor contextAccessor,
        IOutboxSignal signal,
        IOptions<OutboxOptions> options,
        ILogger<DirectSqlOutboxPublisher> logger)
    {
        _transactionAccessor = transactionAccessor;
        _serializer = serializer;
        _contextAccessor = contextAccessor;
        _signal = signal;
        _options = options.Value;
        _logger = logger;
    }

    public async Task PublishAsync(
        string eventType,
        object payload,
        string? correlationId = null,
        string? entityId = null,
        Dictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = OutboxActivitySource.Source.StartActivity("outbox.publish");
        activity?.SetTag("outbox.event_type", eventType);

        var connection = _transactionAccessor.Connection;
        var transaction = _transactionAccessor.Transaction;

        var schema = _options.SchemaName;
        var messageId = Guid.NewGuid();
        var serializedPayload = _serializer.Serialize(payload);
        var headersJson = headers != null ? _serializer.Serialize(headers) : null;
        var traceId = Activity.Current?.TraceId.ToString();

        var sql = $"""
            INSERT INTO [{schema}].[OutboxMessages]
                ([Id], [EventType], [Payload], [CorrelationId], [TraceId], [Status], [RetryCount], [CreatedAt], [Headers], [TenantId], [UserId], [EntityId])
            VALUES
                (@Id, @EventType, @Payload, @CorrelationId, @TraceId, @Status, 0, SYSDATETIMEOFFSET(), @Headers, @TenantId, @UserId, @EntityId)
            """;

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.CommandType = CommandType.Text;

        command.Parameters.Add(new SqlParameter("@Id",            SqlDbType.UniqueIdentifier) { Value = messageId });
        command.Parameters.Add(new SqlParameter("@EventType",     SqlDbType.NVarChar, 256)    { Value = eventType });
        command.Parameters.Add(new SqlParameter("@Payload",       SqlDbType.NVarChar, -1)     { Value = serializedPayload });
        command.Parameters.Add(new SqlParameter("@CorrelationId", SqlDbType.NVarChar, 128)    { Value = (object?)correlationId ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@TraceId",       SqlDbType.NVarChar, 128)    { Value = (object?)traceId       ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@Status",        SqlDbType.Int)              { Value = (int)MessageStatus.Pending });
        command.Parameters.Add(new SqlParameter("@Headers",       SqlDbType.NVarChar, -1)     { Value = (object?)headersJson   ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@TenantId",      SqlDbType.NVarChar, 256)    { Value = (object?)_contextAccessor.TenantId ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@UserId",        SqlDbType.NVarChar, 256)    { Value = (object?)_contextAccessor.UserId   ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@EntityId",      SqlDbType.NVarChar, 256)    { Value = (object?)entityId ?? DBNull.Value });

        await command.ExecuteNonQueryAsync(cancellationToken);

        OutboxMetrics.MessagesPublished.Add(1, new KeyValuePair<string, object?>("event_type", eventType));
        activity?.SetTag("outbox.message_id", messageId.ToString());

        _logger.LogDebug("Published outbox message {MessageId} with event type {EventType}", messageId, eventType);

        // Wake the processor immediately after the INSERT commits with the caller's transaction.
        // Fire-and-forget: does not affect transactional guarantee.
        _signal.Notify(messageId);
    }
}
