using Dragonfire.IdempotentApi.Attributes;
using Dragonfire.IdempotentApi.Interfaces;
using Microsoft.AspNetCore.Http;

namespace Dragonfire.IdempotentApi.Policies;

/// <summary>
/// Opt-in policy: handles only endpoints that carry an <see cref="IdempotentAttribute"/>.
/// Useful when you want explicit per-endpoint declaration rather than method-based defaults.
/// </summary>
public sealed class EndpointAttributeIdempotencyPolicy : IIdempotencyPolicy
{
    public bool ShouldHandle(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        return endpoint?.Metadata.GetMetadata<IdempotentAttribute>() is not null;
    }
}
