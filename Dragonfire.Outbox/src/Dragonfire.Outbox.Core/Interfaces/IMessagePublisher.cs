using Dragonfire.Outbox.Models;

namespace Dragonfire.Outbox.Interfaces;

public interface IMessagePublisher
{
    Task PublishAsync(OutboxMessage message, CancellationToken ct = default);
}
