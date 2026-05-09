using Dragonfire.IdempotentApi.Interfaces;
using Dragonfire.IdempotentApi.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Dragonfire.IdempotentApi.KeyReaders;

/// <summary>
/// Reads the configured request header (default <c>Idempotency-Key</c>).
/// </summary>
public sealed class HeaderIdempotencyKeyReader : IIdempotencyKeyReader
{
    private readonly IdempotentApiOptions _options;

    public HeaderIdempotencyKeyReader(IOptions<IdempotentApiOptions> options) =>
        _options = options.Value;

    public bool TryRead(HttpContext context, out string? key)
    {
        if (context.Request.Headers.TryGetValue(_options.HeaderName, out var values))
        {
            var v = values.ToString();
            if (!string.IsNullOrWhiteSpace(v))
            {
                key = v.Trim();
                return true;
            }
        }
        key = null;
        return false;
    }
}
