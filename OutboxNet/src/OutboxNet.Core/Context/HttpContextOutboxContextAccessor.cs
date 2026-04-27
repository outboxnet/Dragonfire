using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using OutboxNet.Interfaces;
using OutboxNet.Options;

namespace OutboxNet.Context;

/// <summary>
/// Resolves TenantId and UserId from the current HTTP request's claims principal.
/// Register by calling <c>UseHttpContextAccessor()</c> on the outbox builder.
/// </summary>
public sealed class HttpContextOutboxContextAccessor : IOutboxContextAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly OutboxContextOptions _options;

    public HttpContextOutboxContextAccessor(
        IHttpContextAccessor httpContextAccessor,
        IOptions<OutboxContextOptions> options)
    {
        _httpContextAccessor = httpContextAccessor;
        _options = options.Value;
    }

    public string? TenantId => GetClaimValue(_options.TenantIdClaimType);
    public string? UserId => GetClaimValue(_options.UserIdClaimType);

    private string? GetClaimValue(string? claimType)
    {
        if (claimType is null) return null;
        return _httpContextAccessor.HttpContext?.User?.FindFirst(claimType)?.Value;
    }
}
