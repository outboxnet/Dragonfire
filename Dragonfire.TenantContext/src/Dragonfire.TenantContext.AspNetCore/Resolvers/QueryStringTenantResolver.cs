using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Dragonfire.TenantContext.Resolution;

namespace Dragonfire.TenantContext.AspNetCore.Resolvers;

/// <summary>Options for <see cref="QueryStringTenantResolver"/>.</summary>
public sealed class QueryStringTenantResolverOptions
{
    /// <summary>Query string parameter name. Default: <c>tenant</c>.</summary>
    public string ParameterName { get; set; } = "tenant";
}

/// <summary>Resolves the tenant from a query string parameter. Lowest precedence in most setups.</summary>
public sealed class QueryStringTenantResolver : ITenantResolver
{
    private readonly QueryStringTenantResolverOptions _options;

    public QueryStringTenantResolver(IOptions<QueryStringTenantResolverOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value ?? new QueryStringTenantResolverOptions();
    }

    public string Name => $"query:{_options.ParameterName}";

    public ValueTask<TenantResolution> ResolveAsync(TenantResolutionContext context, CancellationToken cancellationToken)
    {
        var http = context.Get<HttpContext>(TenantResolutionContext.HttpContextKey);
        if (http is null) return ValueTask.FromResult(TenantResolution.Unresolved);

        if (!http.Request.Query.TryGetValue(_options.ParameterName, out var values)) return ValueTask.FromResult(TenantResolution.Unresolved);
        if (!TenantId.TryParse(values.ToString(), out var id)) return ValueTask.FromResult(TenantResolution.Unresolved);
        return ValueTask.FromResult(TenantResolution.Resolved(id, Name));
    }
}
