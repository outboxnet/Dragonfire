namespace SagaNet.Core.Abstractions;

/// <summary>
/// In-process event broker used to signal waiting workflow instances.
/// For cross-process signalling, events are persisted via <see cref="IWorkflowRepository"/>.
/// </summary>
public interface IEventBroker
{
    /// <summary>Publishes an event, waking any subscribed waiters immediately.</summary>
    Task PublishAsync(string eventName, string eventKey, object? data, CancellationToken ct = default);

    /// <summary>
    /// Subscribes to a specific event. Returns when the event is received or the token is cancelled.
    /// </summary>
    Task<object?> SubscribeAsync(string eventName, string eventKey, CancellationToken ct = default);
}
