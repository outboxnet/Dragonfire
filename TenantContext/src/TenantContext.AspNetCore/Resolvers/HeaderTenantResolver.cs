using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using TenantContext.Resolution;

namespace TenantContext.AspNetCore.Resolvers;

/// <summary>Options for <see cref="HeaderTenantResolver"/>.</summary>
public sealed class HeaderTenantResolverOptions
{
    /// <summary>Header name to read. Default: <c>X-Tenant-Id</c>.</summary>
    public string HeaderName { get; set; } = "X-Tenant-Id";
}

/// <summary>Reads the tenant id from a configurable HTTP request header.</summary>
public sealed class HeaderTenantResolver : ITenantResolver
{
    private readonly HeaderTenantResolverOptions _options;

    public HeaderTenantResolver(IOptions<HeaderTenantResolverOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value ?? new HeaderTenantResolverOptions();
        if (string.IsNullOrWhiteSpace(_options.HeaderName))
            throw new InvalidOperationException("HeaderTenantResolver: HeaderName is required.");
    }

    public string Name => $"header:{_options.HeaderName}";

    public ValueTask<TenantResolution> ResolveAsync(TenantResolutionContext context, CancellationToken cancellationToken)
    {
        var http = context.Get<HttpContext>(TenantResolutionContext.HttpContextKey);
        if (http is null) return ValueTask.FromResult(TenantResolution.Unresolved);

        if (!http.Request.Headers.TryGetValue(_options.HeaderName, out var values)) return ValueTask.FromResult(TenantResolution.Unresolved);
        var raw = values.ToString();
        if (!TenantId.TryParse(raw, out var id)) return ValueTask.FromResult(TenantResolution.Unresolved);

        return ValueTask.FromResult(TenantResolution.Resolved(id, Name));
    }
}
