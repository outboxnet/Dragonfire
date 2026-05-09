namespace Dragonfire.IdempotentApi.Models;

/// <summary>
/// Discrete result of <see cref="Interfaces.IIdempotencyStore.TryReserveAsync"/>.
/// Drives the middleware's branching: replay, reject, or proceed.
/// </summary>
public enum ReservationKind
{
    /// <summary>First-arriving request — caller should run the handler and then SaveResponseAsync.</summary>
    Acquired,

    /// <summary>Duplicate of a request whose response is already persisted — replay the stored response.</summary>
    AlreadyCompleted,

    /// <summary>Duplicate of a still-running request — return 409 Conflict.</summary>
    InProgress,

    /// <summary>Same key reused with a different request body — return 422 Unprocessable Entity.</summary>
    FingerprintMismatch,
}

/// <summary>
/// Outcome of reserving an idempotency key, plus the existing entry when relevant.
/// </summary>
public sealed class ReservationOutcome
{
    public ReservationKind Kind { get; private init; }
    public IdempotencyEntry? Entry { get; private init; }

    public static ReservationOutcome Acquired() =>
        new() { Kind = ReservationKind.Acquired };

    public static ReservationOutcome AlreadyCompleted(IdempotencyEntry entry) =>
        new() { Kind = ReservationKind.AlreadyCompleted, Entry = entry };

    public static ReservationOutcome InProgress(IdempotencyEntry entry) =>
        new() { Kind = ReservationKind.InProgress, Entry = entry };

    public static ReservationOutcome FingerprintMismatch(IdempotencyEntry entry) =>
        new() { Kind = ReservationKind.FingerprintMismatch, Entry = entry };
}
