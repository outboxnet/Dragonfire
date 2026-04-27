using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OutboxNet.Interfaces;
using OutboxNet.Models;
using OutboxNet.Observability;
using OutboxNet.Options;

namespace OutboxNet.EntityFrameworkCore;

internal sealed class EfCoreOutboxPublisher<TDbContext> : IOutboxPublisher where TDbContext : DbContext
{
    private readonly TDbContext _userDbContext;
    private readonly OutboxDbContext _outboxDbContext;
    private readonly IMessageSerializer _serializer;
    private readonly IOutboxContextAccessor _contextAccessor;
    private readonly IOutboxSignal _signal;
    private readonly OutboxOptions _options;
    private readonly ILogger<EfCoreOutboxPublisher<TDbContext>> _logger;

    public EfCoreOutboxPublisher(
        TDbContext userDbContext,
        OutboxDbContext outboxDbContext,
        IMessageSerializer serializer,
        IOutboxContextAccessor contextAccessor,
        IOutboxSignal signal,
        IOptions<OutboxOptions> options,
        ILogger<EfCoreOutboxPublisher<TDbContext>> logger)
    {
        _userDbContext = userDbContext;
        _outboxDbContext = outboxDbContext;
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

        var transaction = _userDbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "No active transaction found. Wrap your operation in a transaction using Database.BeginTransactionAsync().");

        // Enlist the OutboxDbContext in the user's existing transaction so the
        // outbox INSERT is atomic with the caller's domain writes.
        // Guard: if PublishAsync is called twice in the same scope the connection is
        // already set — calling SetDbConnection again would be a no-op on the same
        // object but avoids any EF Core internal state confusion.
        var dbConnection = _userDbContext.Database.GetDbConnection();
        var dbTransaction = transaction.GetDbTransaction();
        if (_outboxDbContext.Database.GetDbConnection() != dbConnection)
        {
            // contextOwnsConnection: false — the user's DbContext owns the connection lifetime.
            _outboxDbContext.Database.SetDbConnection(dbConnection, contextOwnsConnection: false);
        }
        await _outboxDbContext.Database.UseTransactionAsync(dbTransaction, cancellationToken);

        var messageId = Guid.NewGuid();
        var traceId = Activity.Current?.TraceId.ToString();

        var message = new OutboxMessage
        {
            Id = messageId,
            EventType = eventType,
            Payload = _serializer.Serialize(payload),
            CorrelationId = correlationId,
            TraceId = traceId,
            Status = MessageStatus.Pending,
            RetryCount = 0,
            CreatedAt = DateTimeOffset.UtcNow,
            Headers = headers,
            TenantId = _contextAccessor.TenantId,
            UserId = _contextAccessor.UserId,
            EntityId = entityId
        };

        _outboxDbContext.OutboxMessages.Add(message);
        await _outboxDbContext.SaveChangesAsync(cancellationToken);

        OutboxMetrics.MessagesPublished.Add(1, new KeyValuePair<string, object?>("event_type", eventType));
        activity?.SetTag("outbox.message_id", messageId.ToString());

        _logger.LogDebug("Published outbox message {MessageId} with event type {EventType}", messageId, eventType);

        // Wake the processor immediately — eliminates polling-interval latency for the
        // first message after an idle period. The signal is fire-and-forget; it does not
        // affect the transactional guarantee (message is already committed).
        _signal.Notify(messageId);
    }
}
