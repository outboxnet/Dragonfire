using OutboxNet.Interfaces;
using OutboxNet.Models;

namespace OutboxNet.Subscriptions;

/// <summary>
/// Fallback <see cref="ISubscriptionReader"/> registered by <c>AddOutboxNet()</c>.
/// Returns empty subscription lists so the processor starts without crashing when
/// no store extension (UseSqlServerContext, UseDirectSqlServer, UseConfigWebhooks) is called.
/// Messages are marked as delivered with no recipients — a warning is logged per message.
/// Replace this by calling one of the store extensions.
/// </summary>
internal sealed class NullSubscriptionReader : ISubscriptionReader
{
    public Task<IReadOnlyList<WebhookSubscription>> GetForMessageAsync(
        OutboxMessage message, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<WebhookSubscription>>([]);

    public Task<IReadOnlyList<WebhookSubscription>> GetByEventTypeAsync(
        string eventType, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<WebhookSubscription>>([]);

    public Task<WebhookSubscription?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult<WebhookSubscription?>(null);
}
