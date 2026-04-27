using System;
using Dragonfire.Logging.Grpc.Configuration;
using Dragonfire.Logging.Grpc.Interceptors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dragonfire.Logging.Grpc.Extensions
{
    /// <summary>
    /// Extension methods for registering Dragonfire gRPC logging interceptors in DI.
    /// </summary>
    public static class DragonfireGrpcExtensions
    {
        /// <summary>
        /// Registers <see cref="DragonfireServerLoggingInterceptor"/> and its options in the
        /// DI container.
        ///
        /// <para>After calling this method you must also add the interceptor to the gRPC server
        /// pipeline. The cleanest way is via <c>AddGrpc</c>:</para>
        /// <code>
        /// builder.Services.AddDragonfireGrpcServerLogging(options =>
        /// {
        ///     options.LogRequestFields  = true;
        ///     options.LogResponseFields = true;
        ///     options.ExcludeFields.Add("authToken");
        /// });
        ///
        /// builder.Services.AddGrpc(options =>
        ///     options.Interceptors.Add&lt;DragonfireServerLoggingInterceptor&gt;());
        /// </code>
        ///
        /// <para>To apply the interceptor only to specific services:</para>
        /// <code>
        /// builder.Services.AddGrpc();
        /// builder.Services.AddGrpcServiceOptions&lt;GreeterService&gt;(options =>
        ///     options.Interceptors.Add&lt;DragonfireServerLoggingInterceptor&gt;());
        /// </code>
        /// </summary>
        public static IServiceCollection AddDragonfireGrpcServerLogging(
            this IServiceCollection services,
            Action<DragonfireGrpcServerOptions>? configure = null)
        {
            var options = new DragonfireGrpcServerOptions();
            configure?.Invoke(options);
            services.TryAddSingleton(options);
            services.TryAddSingleton<DragonfireServerLoggingInterceptor>();
            return services;
        }

        /// <summary>
        /// Registers <see cref="DragonfireClientLoggingInterceptor"/> and its options in the
        /// DI container.
        ///
        /// <para>After calling this method you must also add the interceptor to each gRPC
        /// client factory pipeline where logging is desired:</para>
        /// <code>
        /// builder.Services.AddDragonfireGrpcClientLogging(options =>
        /// {
        ///     options.LogRequestFields  = true;
        ///     options.LogResponseFields = true;
        ///     options.ExcludeFields.Add("authToken");
        /// });
        ///
        /// builder.Services.AddGrpcClient&lt;GreeterClient&gt;(o =>
        ///         o.Address = new Uri("https://grpc-backend:5001"))
        ///     .AddInterceptor&lt;DragonfireClientLoggingInterceptor&gt;();
        ///
        /// builder.Services.AddGrpcClient&lt;OrderClient&gt;(o =>
        ///         o.Address = new Uri("https://order-service:5002"))
        ///     .AddInterceptor&lt;DragonfireClientLoggingInterceptor&gt;();
        /// </code>
        /// </summary>
        public static IServiceCollection AddDragonfireGrpcClientLogging(
            this IServiceCollection services,
            Action<DragonfireGrpcClientOptions>? configure = null)
        {
            var options = new DragonfireGrpcClientOptions();
            configure?.Invoke(options);
            services.TryAddSingleton(options);
            services.TryAddSingleton<DragonfireClientLoggingInterceptor>();
            return services;
        }
    }
}
