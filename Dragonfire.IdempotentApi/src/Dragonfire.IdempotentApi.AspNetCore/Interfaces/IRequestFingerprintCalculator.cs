using Microsoft.AspNetCore.Http;

namespace Dragonfire.IdempotentApi.Interfaces;

/// <summary>
/// Computes a stable fingerprint of a request — used to detect when the same
/// idempotency key is reused with a different request body (a client bug that
/// must be surfaced as 422 Unprocessable Entity rather than silently replaying
/// the wrong response).
/// </summary>
public interface IRequestFingerprintCalculator
{
    Task<string> CalculateAsync(HttpContext context, CancellationToken ct);
}
