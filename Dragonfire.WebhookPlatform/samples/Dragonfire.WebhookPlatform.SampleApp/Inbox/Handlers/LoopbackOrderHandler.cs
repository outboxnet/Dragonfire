using Dragonfire.Inbox.Extensions;
using Dragonfire.Inbox.Interfaces;
using Dragonfire.Inbox.Models;

namespace Dragonfire.WebhookPlatform.SampleApp.Inbox.Handlers;

/// <summary>
/// Inbox handler invoked when the loopback receiver successfully persists a new message.
/// Demonstrates that the same event the application emitted has now been received and
/// routed to a typed handler — the inbox row records the success, so reruns are skipped.
/// </summary>
public sealed class LoopbackOrderHandler : IInboxHandler
{
    private readonly ILogger<LoopbackOrderHandler> _logger;

    public LoopbackOrderHandler(ILogger<LoopbackOrderHandler> logger) => _logger = logger;

    public Task HandleAsync(InboxMessage message, CancellationToken ct)
    {
        var dto = message.PayloadAs<OrderCreatedDto>();
        _logger.LogInformation(
            "Loopback inbox received {EventType} order={OrderId} tenant={TenantId} total={Total} {Currency}",
            message.EventType, dto?.OrderId, message.TenantId, dto?.Total, dto?.Currency);
        return Task.CompletedTask;
    }

    private sealed record OrderCreatedDto(
        Guid OrderId,
        string? TenantId,
        string? CustomerId,
        decimal Total,
        string? Currency);
}
