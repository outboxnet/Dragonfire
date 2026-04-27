using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using TenantContext.Resolution;

namespace TenantContext.AspNetCore.Resolvers;

/// <summary>Options for <see cref="SubdomainTenantResolver"/>.</summary>
public sealed class SubdomainTenantResolverOptions
{
    /// <summary>
    /// Root host(s) the application serves under (e.g. <c>example.com</c>). The tenant is the
    /// left-most label that is NOT in this set. Multiple roots are supported for multi-region apps.
    /// </summary>
    public IList<string> RootHosts { get; set; } = new List<string>();

    /// <summary>Subdomains that should NOT be treated as tenant ids (e.g. <c>www</c>, <c>api</c>, <c>admin</c>).</summary>
    public ISet<string> ExcludedSubdomains { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "www", "api", "admin" };
}

/// <summary>Resolves the tenant from the left-most subdomain of the request host.</summary>
public sealed class SubdomainTenantResolver : ITenantResolver
{
    private readonly SubdomainTenantResolverOptions _options;

    public SubdomainTenantResolver(IOptions<SubdomainTenantResolverOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value ?? new SubdomainTenantResolverOptions();
    }

    public string Name => "subdomain";

    public ValueTask<TenantResolution> ResolveAsync(TenantResolutionContext context, CancellationToken cancellationToken)
    {
        var http = context.Get<HttpContext>(TenantResolutionContext.HttpContextKey);
        if (http is null) return ValueTask.FromResult(TenantResolution.Unresolved);

        var host = http.Request.Host.Host;
        if (string.IsNullOrEmpty(host)) return ValueTask.FromResult(TenantResolution.Unresolved);

        // Find a matching root host (case-insensitive). If none configured, treat the first label as tenant.
        string? remainder = null;
        foreach (var root in _options.RootHosts)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            if (host.Equals(root, StringComparison.OrdinalIgnoreCase))
                return ValueTask.FromResult(TenantResolution.Unresolved); // bare root, no subdomain
            var suffix = "." + root;
            if (host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                remainder = host[..^suffix.Length];
                break;
            }
        }

        if (remainder is null)
        {
            // No configured root matched. Fall back to first DNS label if host has at least 3 labels.
            var dot = host.IndexOf('.');
            if (dot <= 0) return ValueTask.FromResult(TenantResolution.Unresolved);
            remainder = host[..dot];
        }

        // Take the right-most label of the remainder (closest to root) as the tenant.
        var lastDot = remainder.LastIndexOf('.');
        var label = lastDot < 0 ? remainder : remainder[(lastDot + 1)..];

        if (string.IsNullOrWhiteSpace(label) || _options.ExcludedSubdomains.Contains(label))
            return ValueTask.FromResult(TenantResolution.Unresolved);

        return ValueTask.FromResult(TenantResolution.Resolved(new TenantId(label), Name));
    }
}
