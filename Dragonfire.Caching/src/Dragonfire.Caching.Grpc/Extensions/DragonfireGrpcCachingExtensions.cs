using System;
using Dragonfire.Caching.Grpc.Configuration;
using Dragonfire.Caching.Grpc.Interceptors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dragonfire.Caching.Grpc.Extensions
{
    /// <summary>
    /// Extension methods for registering Dragonfire gRPC caching interceptors in DI.
    /// Both interceptors require the core caching services (<c>AddDragonfireCaching()</c>
    /// + a provider) to already be registered.
    /// </summary>
    public static class DragonfireGrpcCachingExtensions
    {
        /// <summary>
        /// Registers <see cref="DragonfireClientCachingInterceptor"/> and its options.
        /// After this you must add the interceptor to each gRPC client factory pipeline:
        ///
        /// <code>
        /// builder.Services.AddDragonfireGrpcClientCaching(options =>
        /// {
        ///     options.Cache(new GrpcCacheRule {
        ///         FullMethod        = "/order.OrderService/GetOrder",
        ///         KeyTemplate       = "order:{tenantId}:{orderId}",
        ///         SlidingExpiration = TimeSpan.FromMinutes(5)
        ///     });
        /// });
        ///
        /// builder.Services.AddGrpcClient&lt;OrderService.OrderServiceClient&gt;(o =>
        ///         o.Address = new Uri("https://order-service:5001"))
        ///     .AddInterceptor&lt;DragonfireClientCachingInterceptor&gt;();
        /// </code>
        /// </summary>
        public static IServiceCollection AddDragonfireGrpcClientCaching(
            this IServiceCollection services,
            Action<DragonfireGrpcCachingClientOptions>? configure = null)
        {
            var options = new DragonfireGrpcCachingClientOptions();
            configure?.Invoke(options);
            services.TryAddSingleton(options);
            services.TryAddSingleton<DragonfireClientCachingInterceptor>();
            return services;
        }

        /// <summary>
        /// Registers <see cref="DragonfireServerCachingInterceptor"/> and its options.
        /// After this you must add the interceptor to the gRPC server pipeline:
        ///
        /// <code>
        /// builder.Services.AddDragonfireGrpcServerCaching(options =>
        /// {
        ///     options.Cache(new GrpcCacheRule {
        ///         FullMethod        = "/order.OrderService/GetOrder",
        ///         KeyTemplate       = "order:{tenantId}:{orderId}",
        ///         SlidingExpiration = TimeSpan.FromMinutes(5)
        ///     });
        /// });
        ///
        /// builder.Services.AddGrpc(o =>
        ///     o.Interceptors.Add&lt;DragonfireServerCachingInterceptor&gt;());
        /// </code>
        /// </summary>
        public static IServiceCollection AddDragonfireGrpcServerCaching(
            this IServiceCollection services,
            Action<DragonfireGrpcCachingServerOptions>? configure = null)
        {
            var options = new DragonfireGrpcCachingServerOptions();
            configure?.Invoke(options);
            services.TryAddSingleton(options);
            services.TryAddSingleton<DragonfireServerCachingInterceptor>();
            return services;
        }
    }
}
