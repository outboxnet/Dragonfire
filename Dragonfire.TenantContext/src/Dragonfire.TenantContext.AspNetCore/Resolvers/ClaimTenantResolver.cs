using System.Security.Claims;
using Microsoft.Extensions.Options;
using Dragonfire.TenantContext.Resolution;

namespace Dragonfire.TenantContext.AspNetCore.Resolvers;

/// <summary>Options for <see cref="ClaimTenantResolver"/>.</summary>
public sealed class ClaimTenantResolverOptions
{
    /// <summary>Claim type holding the tenant id. Default: <c>tid</c> (Microsoft tenant id claim).</summary>
    public string ClaimType { get; set; } = "tid";

    /// <summary>When set, only this authentication scheme's identity is consulted; otherwise the primary identity.</summary>
    public string? AuthenticationScheme { get; set; }
}

/// <summary>
/// Resolves the tenant from a configurable claim on the authenticated principal. Works with any
/// auth scheme that produces a <see cref="ClaimsPrincipal"/> (JWT bearer, cookies, OIDC, etc.).
/// </summary>
public sealed class ClaimTenantResolver : ITenantResolver
{
    private readonly ClaimTenantResolverOptions _options;

    public ClaimTenantResolver(IOptions<ClaimTenantResolverOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value ?? new ClaimTenantResolverOptions();
        if (string.IsNullOrWhiteSpace(_options.ClaimType))
            throw new InvalidOperationException("ClaimTenantResolver: ClaimType is required.");
    }

    public string Name => $"claim:{_options.ClaimType}";

    public ValueTask<TenantResolution> ResolveAsync(TenantResolutionContext context, CancellationToken cancellationToken)
    {
        var principal = context.Get<ClaimsPrincipal>(TenantResolutionContext.PrincipalKey);
        if (principal?.Identity?.IsAuthenticated != true) return ValueTask.FromResult(TenantResolution.Unresolved);

        Claim? claim;
        if (_options.AuthenticationScheme is null)
        {
            claim = principal.FindFirst(_options.ClaimType);
        }
        else
        {
            claim = principal.Identities
                .Where(i => string.Equals(i.AuthenticationType, _options.AuthenticationScheme, StringComparison.Ordinal))
                .Select(i => i.FindFirst(_options.ClaimType))
                .FirstOrDefault(c => c is not null);
        }

        if (claim is null || !TenantId.TryParse(claim.Value, out var id)) return ValueTask.FromResult(TenantResolution.Unresolved);
        return ValueTask.FromResult(TenantResolution.Resolved(id, Name));
    }
}
