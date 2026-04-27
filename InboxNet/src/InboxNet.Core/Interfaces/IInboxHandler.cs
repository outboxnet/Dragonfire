using InboxNet.Models;

namespace InboxNet.Interfaces;

/// <summary>
/// Application-defined handler for an inbox message. Implementations are registered via
/// <c>AddHandler&lt;T&gt;</c> on the inbox builder; multiple handlers can target the same
/// <c>(ProviderKey, EventType)</c> pair and run sequentially in registration order.
/// </summary>
/// <remarks>
/// Handlers are resolved from a per-message DI scope, so they can safely inject scoped
/// services such as a <c>DbContext</c>. A throwing handler marks only itself as failed —
/// other handlers registered for the same message still run (unless they have already
/// succeeded on a prior attempt).
/// </remarks>
public interface IInboxHandler
{
    Task HandleAsync(InboxMessage message, CancellationToken ct);
}
