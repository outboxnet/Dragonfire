namespace Dragonfire.IdempotentApi.Models;

/// <summary>
/// In-memory representation of a stored idempotency record. Storage backends translate
/// to/from this domain object — the rest of the library only sees this shape.
/// </summary>
public sealed class IdempotencyEntry
{
    public string Key { get; init; } = default!;
    public string Fingerprint { get; init; } = default!;
    public IdempotencyStatus Status { get; set; }
    public IdempotentResponse? Response { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; set; }
}
