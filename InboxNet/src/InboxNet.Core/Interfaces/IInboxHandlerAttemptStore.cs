using InboxNet.Models;

namespace InboxNet.Interfaces;

/// <summary>
/// Summary of prior handler attempts for a single inbox message.
/// Retrieved in one batch query via <see cref="IInboxHandlerAttemptStore.GetHandlerStatesAsync"/>.
/// </summary>
public sealed record HandlerAttemptState(int AttemptCount, bool HasSuccess);

public interface IInboxHandlerAttemptStore
{
    Task SaveAttemptAsync(InboxHandlerAttempt attempt, CancellationToken ct = default);

    /// <summary>
    /// Default implementation is sequential — a parallel <c>Task.WhenAll</c> over a single
    /// scoped store would race the underlying connection. Implementations backed by a
    /// thread-safe transport (e.g. one connection per attempt) may override.
    /// </summary>
    async Task SaveAttemptsAsync(IReadOnlyList<InboxHandlerAttempt> attempts, CancellationToken ct = default)
    {
        if (attempts.Count == 0) return;
        for (var i = 0; i < attempts.Count; i++)
            await SaveAttemptAsync(attempts[i], ct);
    }

    Task<IReadOnlyList<InboxHandlerAttempt>> GetByMessageIdAsync(Guid messageId, CancellationToken ct = default);

    /// <summary>
    /// Returns the attempt count and success status for every handler name in
    /// <paramref name="handlerNames"/> in a single round-trip.
    /// Handlers with no prior attempts are absent from the result.
    /// </summary>
    Task<IReadOnlyDictionary<string, HandlerAttemptState>> GetHandlerStatesAsync(
        Guid messageId,
        IReadOnlyList<string> handlerNames,
        CancellationToken ct = default);

    Task<int> PurgeOldAttemptsAsync(DateTimeOffset olderThan, CancellationToken ct = default);
}
