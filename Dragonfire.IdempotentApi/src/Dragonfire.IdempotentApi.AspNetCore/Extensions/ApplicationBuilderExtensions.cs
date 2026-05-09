using Dragonfire.IdempotentApi.Middleware;
using Microsoft.AspNetCore.Builder;

namespace Dragonfire.IdempotentApi.Extensions;

public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Registers the idempotency middleware. Place AFTER <c>UseRouting</c> so the
    /// endpoint-attribute policy can see the matched endpoint.
    /// </summary>
    public static IApplicationBuilder UseIdempotentApi(this IApplicationBuilder app) =>
        app.UseMiddleware<IdempotencyMiddleware>();
}
