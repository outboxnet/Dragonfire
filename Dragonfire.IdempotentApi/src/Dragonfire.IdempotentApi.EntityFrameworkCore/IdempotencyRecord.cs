using Dragonfire.IdempotentApi.Models;

namespace Dragonfire.IdempotentApi.EntityFrameworkCore;

/// <summary>
/// EF Core entity that persists an <see cref="IdempotencyEntry"/>. The response is
/// flattened into discrete columns so the row stays queryable / indexable.
/// </summary>
public sealed class IdempotencyRecord
{
    public string Key { get; set; } = default!;
    public string Fingerprint { get; set; } = default!;
    public IdempotencyStatus Status { get; set; }

    public int? StatusCode { get; set; }
    public string? ContentType { get; set; }
    public byte[]? ResponseBody { get; set; }
    public string? ResponseHeadersJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Row-version for optimistic concurrency on response writes.</summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
