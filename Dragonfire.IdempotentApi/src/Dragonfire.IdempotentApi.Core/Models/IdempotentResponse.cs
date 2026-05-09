namespace Dragonfire.IdempotentApi.Models;

/// <summary>
/// A captured HTTP response that can be replayed for duplicate idempotent requests.
/// Storage-backend agnostic — providers serialize/deserialize this as needed.
/// </summary>
public sealed class IdempotentResponse
{
    public int StatusCode { get; init; }
    public string? ContentType { get; init; }
    public byte[] Body { get; init; } = Array.Empty<byte>();
    public IDictionary<string, string[]> Headers { get; init; } =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
}
