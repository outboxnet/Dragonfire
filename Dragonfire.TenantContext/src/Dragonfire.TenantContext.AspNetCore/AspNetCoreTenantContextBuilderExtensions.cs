using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Dragonfire.TenantContext.AspNetCore.Resolvers;
using Dragonfire.TenantContext.DependencyInjection;
using Dragonfire.TenantContext.Resolution;

namespace Dragonfire.TenantContext.AspNetCore;

/// <summary>
/// ASP.NET Core extensions for <see cref="TenantContextBuilder"/>. Each method registers a single
/// resolver — combine as many as needed; resolution order is the call order.
/// </summary>
public static class AspNetCoreTenantContextBuilderExtensions
{
    public static TenantContextBuilder AddHttpOptions(this TenantContextBuilder builder, Action<TenantContextHttpOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddOptions<TenantContextHttpOptions>();
        if (configure is not null) builder.Services.Configure(configure);
        return builder;
    }

    public static TenantContextBuilder AddHeaderResolver(this TenantContextBuilder builder, Action<HeaderTenantResolverOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddOptions<HeaderTenantResolverOptions>();
        if (configure is not null) builder.Services.Configure(configure);
        builder.Services.AddSingleton<ITenantResolver, HeaderTenantResolver>();
        return builder;
    }

    public static TenantContextBuilder AddSubdomainResolver(this TenantContextBuilder builder, Action<SubdomainTenantResolverOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        builder.Services.AddOptions<SubdomainTenantResolverOptions>().Configure(configure);
        builder.Services.AddSingleton<ITenantResolver, SubdomainTenantResolver>();
        return builder;
    }

    public static TenantContextBuilder AddClaimResolver(this TenantContextBuilder builder, Action<ClaimTenantResolverOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddOptions<ClaimTenantResolverOptions>();
        if (configure is not null) builder.Services.Configure(configure);
        builder.Services.AddSingleton<ITenantResolver, ClaimTenantResolver>();
        return builder;
    }

    public static TenantContextBuilder AddRouteResolver(this TenantContextBuilder builder, Action<RouteValueTenantResolverOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddOptions<RouteValueTenantResolverOptions>();
        if (configure is not null) builder.Services.Configure(configure);
        builder.Services.AddSingleton<ITenantResolver, RouteValueTenantResolver>();
        return builder;
    }

    public static TenantContextBuilder AddQueryStringResolver(this TenantContextBuilder builder, Action<QueryStringTenantResolverOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddOptions<QueryStringTenantResolverOptions>();
        if (configure is not null) builder.Services.Configure(configure);
        builder.Services.AddSingleton<ITenantResolver, QueryStringTenantResolver>();
        return builder;
    }

    /// <summary>Adds API-key-to-tenant resolution. Caller supplies <typeparamref name="TLookup"/>.</summary>
    public static TenantContextBuilder AddApiKeyResolver<TLookup>(this TenantContextBuilder builder, Action<ApiKeyTenantResolverOptions>? configure = null)
        where TLookup : class, IApiKeyTenantLookup
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddOptions<ApiKeyTenantResolverOptions>();
        if (configure is not null) builder.Services.Configure(configure);
        builder.Services.AddSingleton<IApiKeyTenantLookup, TLookup>();
        builder.Services.AddSingleton<ITenantResolver, ApiKeyTenantResolver>();
        return builder;
    }
}
