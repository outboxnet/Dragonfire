using Microsoft.AspNetCore.Http;

namespace Dragonfire.IdempotentApi.Interfaces;

/// <summary>
/// Decides whether a request participates in idempotency processing. Default
/// implementations match on HTTP method or on an <c>[Idempotent]</c> attribute.
/// </summary>
public interface IIdempotencyPolicy
{
    bool ShouldHandle(HttpContext context);
}
