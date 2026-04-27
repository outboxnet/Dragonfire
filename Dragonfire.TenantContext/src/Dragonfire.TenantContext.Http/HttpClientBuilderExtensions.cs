using Microsoft.Extensions.DependencyInjection;
using Dragonfire.TenantContext.DependencyInjection;

namespace Dragonfire.TenantContext.Http;

/// <summary>Registration helpers for tenant propagation on outbound HttpClients.</summary>
public static class TenantContextHttpExtensions
{
    /// <summary>Configure <see cref="TenantPropagationOptions"/>.</summary>
    public static TenantContextBuilder AddHttpPropagation(this TenantContextBuilder builder, Action<TenantPropagationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddOptions<TenantPropagationOptions>();
        if (configure is not null) builder.Services.Configure(configure);
        builder.Services.AddTransient<TenantPropagationHandler>();
        return builder;
    }

    /// <summary>Adds <see cref="TenantPropagationHandler"/> to a specific <see cref="IHttpClientBuilder"/>.</summary>
    public static IHttpClientBuilder AddTenantPropagation(this IHttpClientBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddTransient<TenantPropagationHandler>();
        return builder.AddHttpMessageHandler<TenantPropagationHandler>();
    }
}
