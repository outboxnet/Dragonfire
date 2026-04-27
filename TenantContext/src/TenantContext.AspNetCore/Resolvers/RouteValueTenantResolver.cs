using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using TenantContext.Resolution;

namespace TenantContext.AspNetCore.Resolvers;

/// <summary>Options for <see cref="RouteValueTenantResolver"/>.</summary>
public sealed class RouteValueTenantResolverOptions
{
    /// <summary>Route value name holding the tenant id. Default: <c>tenant</c>.</summary>
    public string RouteValueName { get; set; } = "tenant";
}

/// <summary>
/// Resolves the tenant from a route value (e.g. <c>/{tenant}/orders</c>). Note: route values are
/// only available after routing has matched; place <c>UseTenantContext</c> after <c>UseRouting</c>.
/// </summary>
public sealed class RouteValueTenantResolver : ITenantResolver
{
    private readonly RouteValueTenantResolverOptions _options;

    public RouteValueTenantResolver(IOptions<RouteValueTenantResolverOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value ?? new RouteValueTenantResolverOptions();
    }

    public string Name => $"route:{_options.RouteValueName}";

    public ValueTask<TenantResolution> ResolveAsync(TenantResolutionContext context, CancellationToken cancellationToken)
    {
        var http = context.Get<HttpContext>(TenantResolutionContext.HttpContextKey);
        if (http is null) return ValueTask.FromResult(TenantResolution.Unresolved);

        if (!http.Request.RouteValues.TryGetValue(_options.RouteValueName, out var raw)) return ValueTask.FromResult(TenantResolution.Unresolved);
        if (!TenantId.TryParse(raw?.ToString(), out var id)) return ValueTask.FromResult(TenantResolution.Unresolved);
        return ValueTask.FromResult(TenantResolution.Resolved(id, Name));
    }
}
