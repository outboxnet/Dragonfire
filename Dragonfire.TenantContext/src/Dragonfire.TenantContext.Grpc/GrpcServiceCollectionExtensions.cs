using Microsoft.Extensions.DependencyInjection;
using Dragonfire.TenantContext.DependencyInjection;
using Dragonfire.TenantContext.Resolution;

namespace Dragonfire.TenantContext.Grpc;

public static class GrpcTenantContextExtensions
{
    /// <summary>Configures gRPC tenant options + registers the metadata-based server resolver.</summary>
    public static TenantContextBuilder AddGrpcServer(this TenantContextBuilder builder, Action<GrpcTenantOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddOptions<GrpcTenantOptions>();
        if (configure is not null) builder.Services.Configure(configure);
        builder.Services.AddSingleton<ITenantResolver, GrpcMetadataTenantResolver>();
        builder.Services.AddSingleton<TenantServerInterceptor>();
        return builder;
    }

    /// <summary>Registers the gRPC client interceptor (caller wires it into channels/clients).</summary>
    public static TenantContextBuilder AddGrpcClient(this TenantContextBuilder builder, Action<GrpcTenantOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddOptions<GrpcTenantOptions>();
        if (configure is not null) builder.Services.Configure(configure);
        builder.Services.AddSingleton<TenantClientInterceptor>();
        return builder;
    }
}
