using Dragonfire.IdempotentApi.Interfaces;
using Dragonfire.IdempotentApi.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Dragonfire.IdempotentApi.Policies;

/// <summary>
/// Default policy: handles requests whose method is in
/// <see cref="IdempotentApiOptions.Methods"/>.
/// </summary>
public sealed class HttpMethodIdempotencyPolicy : IIdempotencyPolicy
{
    private readonly HashSet<string> _methods;

    public HttpMethodIdempotencyPolicy(IOptions<IdempotentApiOptions> options)
    {
        _methods = new HashSet<string>(options.Value.Methods, StringComparer.OrdinalIgnoreCase);
    }

    public bool ShouldHandle(HttpContext context) => _methods.Contains(context.Request.Method);
}
