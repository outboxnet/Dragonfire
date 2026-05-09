using System.Collections.Concurrent;
using Dragonfire.IdempotentApi.Interfaces;
using Dragonfire.IdempotentApi.Models;

namespace Dragonfire.IdempotentApi.InMemory;

/// <summary>
/// Single-process, lock-free idempotency store backed by <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// Suitable for tests, development, and single-instance deployments. Survives only as long
/// as the host process — for multi-instance / durable scenarios use the EF Core store.
/// </summary>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, IdempotencyEntry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;

    public InMemoryIdempotencyStore(TimeProvider? timeProvider = null) =>
        _time = timeProvider ?? TimeProvider.System;

    public Task<ReservationOutcome> TryReserveAsync(
        string key, string fingerprint, DateTimeOffset expiresAt, CancellationToken ct)
    {
        var now = _time.GetUtcNow();

        while (true)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                if (existing.ExpiresAt < now)
                {
                    // Expired — drop and retry the loop so we try to insert fresh.
                    _entries.TryRemove(new KeyValuePair<string, IdempotencyEntry>(key, existing));
                    continue;
                }

                if (!string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
                    return Task.FromResult(ReservationOutcome.FingerprintMismatch(existing));

                return Task.FromResult(existing.Status switch
                {
                    IdempotencyStatus.Completed => ReservationOutcome.AlreadyCompleted(existing),
                    _                            => ReservationOutcome.InProgress(existing),
                });
            }

            var fresh = new IdempotencyEntry
            {
                Key         = key,
                Fingerprint = fingerprint,
                Status      = IdempotencyStatus.Reserved,
                CreatedAt   = now,
                ExpiresAt   = expiresAt,
            };

            if (_entries.TryAdd(key, fresh))
                return Task.FromResult(ReservationOutcome.Acquired());

            // Lost the TryAdd race; loop and treat the winner as an existing entry.
        }
    }

    public Task SaveResponseAsync(string key, IdempotentResponse response, CancellationToken ct)
    {
        if (!_entries.TryGetValue(key, out var entry))
            throw new InvalidOperationException($"No reservation found for idempotency key '{key}'.");

        entry.Status = IdempotencyStatus.Completed;
        entry.Response = response;
        return Task.CompletedTask;
    }

    public Task ReleaseReservationAsync(string key, CancellationToken ct)
    {
        // Only release if it's still in the Reserved state — completed responses must be kept.
        if (_entries.TryGetValue(key, out var entry) && entry.Status == IdempotencyStatus.Reserved)
            _entries.TryRemove(new KeyValuePair<string, IdempotencyEntry>(key, entry));

        return Task.CompletedTask;
    }
}
