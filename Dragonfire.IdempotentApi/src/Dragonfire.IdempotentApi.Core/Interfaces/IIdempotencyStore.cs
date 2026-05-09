using Dragonfire.IdempotentApi.Models;

namespace Dragonfire.IdempotentApi.Interfaces;

/// <summary>
/// Storage abstraction for idempotency records. Implementations must guarantee that
/// <see cref="TryReserveAsync"/> is atomic — concurrent calls for the same key must
/// see exactly one <see cref="ReservationKind.Acquired"/> result.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Atomically reserve <paramref name="key"/> for the caller, or return an outcome
    /// describing the existing reservation.
    /// </summary>
    Task<ReservationOutcome> TryReserveAsync(
        string key,
        string fingerprint,
        DateTimeOffset expiresAt,
        CancellationToken ct);

    /// <summary>
    /// Persist the response for a previously-reserved key and mark it
    /// <see cref="IdempotencyStatus.Completed"/>.
    /// </summary>
    Task SaveResponseAsync(string key, IdempotentResponse response, CancellationToken ct);

    /// <summary>
    /// Drop a reservation that did not complete (e.g., handler threw) so a retry can
    /// claim the key. Idempotent — silent on missing rows.
    /// </summary>
    Task ReleaseReservationAsync(string key, CancellationToken ct);
}
