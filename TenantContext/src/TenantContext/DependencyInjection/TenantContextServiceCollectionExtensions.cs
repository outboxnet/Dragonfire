using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TenantContext.Resolution;
using TenantContext.Serialization;

namespace TenantContext.DependencyInjection;

/// <summary>
/// Entry point for tenant context registration. Adapters extend the returned
/// <see cref="TenantContextBuilder"/> rather than re-registering core services, ensuring a
/// single source of truth.
/// </summary>
public static class TenantContextServiceCollectionExtensions
{
    /// <summary>
    /// Registers core tenant context services:
    /// <list type="bullet">
    ///   <item><description>Singleton <see cref="ITenantContextAccessor"/> + <see cref="ITenantContextSetter"/> backed by AsyncLocal.</description></item>
    ///   <item><description>Singleton <see cref="ITenantResolutionPipeline"/> (<see cref="CompositeTenantResolver"/>) using whatever <see cref="ITenantResolver"/>s are registered.</description></item>
    ///   <item><description>Singleton <see cref="ITenantContextSerializer"/> (<see cref="JsonTenantContextSerializer"/>).</description></item>
    /// </list>
    /// All registrations use <c>TryAdd</c> so callers can override individual pieces.
    /// </summary>
    public static TenantContextBuilder AddTenantContext(this IServiceCollection services, Action<TenantResolutionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<TenantResolutionOptions>();
        if (configure is not null) services.Configure(configure);

        services.TryAddSingleton<AsyncLocalTenantContext>();
        services.TryAddSingleton<ITenantContextAccessor>(sp => sp.GetRequiredService<AsyncLocalTenantContext>());
        services.TryAddSingleton<ITenantContextSetter>(sp => sp.GetRequiredService<AsyncLocalTenantContext>());
        services.TryAddSingleton<ITenantResolutionPipeline, CompositeTenantResolver>();
        services.TryAddSingleton<ITenantContextSerializer, JsonTenantContextSerializer>();

        return new TenantContextBuilder(services);
    }

    /// <summary>Adds an <see cref="ITenantResolver"/> implementation to the resolution chain (order matters).</summary>
    public static TenantContextBuilder AddResolver<TResolver>(this TenantContextBuilder builder)
        where TResolver : class, ITenantResolver
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<ITenantResolver, TResolver>();
        return builder;
    }

    /// <summary>Adds a delegate-backed resolver to the chain (insertion order is preserved).</summary>
    public static TenantContextBuilder AddResolver(
        this TenantContextBuilder builder,
        string name,
        Func<TenantResolutionContext, CancellationToken, ValueTask<TenantResolution>> resolve)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<ITenantResolver>(_ => new DelegateTenantResolver(name, resolve));
        return builder;
    }

    /// <summary>Registers a static fallback tenant. Place last in the chain.</summary>
    public static TenantContextBuilder AddStaticFallback(this TenantContextBuilder builder, TenantId tenantId, string source = "static")
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<ITenantResolver>(_ => new StaticTenantResolver(tenantId, source));
        return builder;
    }
}
