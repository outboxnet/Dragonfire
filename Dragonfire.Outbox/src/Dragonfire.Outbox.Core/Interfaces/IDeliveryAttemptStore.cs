using Dragonfire.Outbox.Models;

namespace Dragonfire.Outbox.Interfaces;

/// <summary>
/// Per-subscription delivery summary for a single outbox message.
/// Fetched in one batch query via <see cref="IDeliveryAttemptStore.GetDeliveryStatesAsync"/>.
/// </summary>
public sealed record SubscriptionDeliveryState(int AttemptCount, bool HasSuccess);

public interface IDeliveryAttemptStore
{
    Task SaveAttemptAsync(DeliveryAttempt attempt, CancellationToken ct = default);

    /// <summary>
    /// Persists multiple delivery attempts in a single round-trip.
    /// Default implementation falls back to parallel individual saves;
    /// concrete implementations should override with a bulk INSERT for efficiency.
    /// </summary>
    Task SaveAttemptsAsync(IReadOnlyList<DeliveryAttempt> attempts, CancellationToken ct = default)
    {
        if (attempts.Count == 0) return Task.CompletedTask;
        if (attempts.Count == 1) return SaveAttemptAsync(attempts[0], ct);
        return Task.WhenAll(attempts.Select(a => SaveAttemptAsync(a, ct)));
    }

    Task<IReadOnlyList<DeliveryAttempt>> GetByMessageIdAsync(Guid messageId, CancellationToken ct = default);

    Task<IReadOnlyList<DeliveryAttempt>> GetBySubscriptionIdAsync(
        Guid subscriptionId,
        int limit = 50,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the attempt count and success status for every subscription in
    /// <paramref name="subscriptionIds"/> in a single round-trip.
    /// Subscriptions with no attempts are absent from the returned dictionary.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, SubscriptionDeliveryState>> GetDeliveryStatesAsync(
        Guid messageId,
        IReadOnlyList<Guid> subscriptionIds,
        CancellationToken ct = default);

    // Legacy single-subscription helpers kept for backward-compat; the pipeline
    // uses GetDeliveryStatesAsync for efficiency.
    async Task<int> GetAttemptCountAsync(Guid messageId, Guid subscriptionId, CancellationToken ct = default)
    {
        var states = await GetDeliveryStatesAsync(messageId, [subscriptionId], ct);
        return states.TryGetValue(subscriptionId, out var s) ? s.AttemptCount : 0;
    }

    async Task<bool> HasSuccessfulDeliveryAsync(Guid messageId, Guid subscriptionId, CancellationToken ct = default)
    {
        var states = await GetDeliveryStatesAsync(messageId, [subscriptionId], ct);
        return states.TryGetValue(subscriptionId, out var s) && s.HasSuccess;
    }

    /// <summary>
    /// Deletes delivery attempt records whose <c>AttemptedAt</c> is older than
    /// <paramref name="olderThan"/>. Returns the number of rows deleted.
    /// Call periodically (e.g. nightly) to prevent unbounded table growth.
    /// </summary>
    Task<int> PurgeOldAttemptsAsync(DateTimeOffset olderThan, CancellationToken ct = default);
}
