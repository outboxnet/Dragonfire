using InboxNet.Interfaces;
using InboxNet.Models;

namespace InboxNet.SampleApp.Handlers;

/// <summary>
/// Catch-all handler that logs every inbox message. Registered without a provider/event
/// filter, so it runs for every received webhook.
/// </summary>
public sealed class LoggingInboxHandler : IInboxHandler
{
    private readonly ILogger<LoggingInboxHandler> _logger;

    public LoggingInboxHandler(ILogger<LoggingInboxHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(InboxMessage message, CancellationToken ct)
    {
        _logger.LogInformation(
            "Inbox message {MessageId} — provider={ProviderKey} event={EventType} entity={EntityId} tenant={TenantId}",
            message.Id, message.ProviderKey, message.EventType, message.EntityId, message.TenantId);
        return Task.CompletedTask;
    }
}
