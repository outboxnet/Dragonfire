using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Dragonfire.TenantContext.Resolution;

namespace Dragonfire.TenantContext.AspNetCore.Resolvers;

/// <summary>
/// Maps an opaque API key to a tenant id. The library does not own the key store; consumers
/// implement <see cref="IApiKeyTenantLookup"/> against their database, secrets store, or cache.
/// </summary>
public interface IApiKeyTenantLookup
{
    /// <summary>Resolve a key to a tenant. Return <see cref="TenantId.Empty"/> for unknown keys.</summary>
    ValueTask<TenantId> LookupAsync(string apiKey, CancellationToken cancellationToken);
}

/// <summary>Options for <see cref="ApiKeyTenantResolver"/>.</summary>
public sealed class ApiKeyTenantResolverOptions
{
    /// <summary>Header name carrying the API key. Default: <c>X-Api-Key</c>.</summary>
    public string HeaderName { get; set; } = "X-Api-Key";
}

/// <summary>Reads an API key from a header and looks up the tenant via <see cref="IApiKeyTenantLookup"/>.</summary>
public sealed class ApiKeyTenantResolver : ITenantResolver
{
    private readonly IApiKeyTenantLookup _lookup;
    private readonly ApiKeyTenantResolverOptions _options;

    public ApiKeyTenantResolver(IApiKeyTenantLookup lookup, IOptions<ApiKeyTenantResolverOptions> options)
    {
        _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value ?? new ApiKeyTenantResolverOptions();
    }

    public string Name => $"apikey:{_options.HeaderName}";

    public async ValueTask<TenantResolution> ResolveAsync(TenantResolutionContext context, CancellationToken cancellationToken)
    {
        var http = context.Get<HttpContext>(TenantResolutionContext.HttpContextKey);
        if (http is null) return TenantResolution.Unresolved;

        if (!http.Request.Headers.TryGetValue(_options.HeaderName, out var values)) return TenantResolution.Unresolved;
        var key = values.ToString();
        if (string.IsNullOrWhiteSpace(key)) return TenantResolution.Unresolved;

        var id = await _lookup.LookupAsync(key, cancellationToken).ConfigureAwait(false);
        return id.IsEmpty ? TenantResolution.Unresolved : TenantResolution.Resolved(id, Name);
    }
}
