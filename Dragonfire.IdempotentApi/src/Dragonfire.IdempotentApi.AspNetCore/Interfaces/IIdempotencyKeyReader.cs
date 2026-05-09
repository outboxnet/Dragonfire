using Microsoft.AspNetCore.Http;

namespace Dragonfire.IdempotentApi.Interfaces;

/// <summary>
/// Strategy for extracting an idempotency key from an inbound HTTP request.
/// Default implementation reads <see cref="Options.IdempotentApiOptions.HeaderName"/>;
/// custom implementations can derive from query string, body, JWT, etc.
/// </summary>
public interface IIdempotencyKeyReader
{
    bool TryRead(HttpContext context, out string? key);
}
