using OutboxNet.Models;

namespace OutboxNet.Interfaces;

public interface IMessagePublisher
{
    Task PublishAsync(OutboxMessage message, CancellationToken ct = default);
}
