using System;

namespace Dragonfire.Caching.Grpc.Configuration
{
    /// <summary>
    /// Describes how a single unary gRPC method should be cached.
    /// Rules are looked up by <see cref="FullMethod"/> at call time.
    /// </summary>
    public sealed class GrpcCacheRule
    {
        /// <summary>
        /// Fully qualified gRPC method path, e.g. <c>/order.OrderService/GetOrder</c>.
        /// Must include the leading slash and match <c>Grpc.Core.Method.FullName</c>.
        /// </summary>
        public string FullMethod { get; init; } = string.Empty;

        /// <summary>
        /// Optional cache key template. Supports <c>{fieldJsonName}</c> placeholders that
        /// resolve against scalar fields of the proto-generated request message.
        /// When omitted, the key is auto-generated as
        /// <c>ServiceName.MethodName(field=value,...)</c> from <see cref="IncludeFields"/>
        /// (or from all scalar fields if <see cref="IncludeFields"/> is empty).
        /// </summary>
        public string? KeyTemplate { get; init; }

        /// <summary>
        /// Absolute time-to-live for the cached response.
        /// Mutually exclusive with <see cref="SlidingExpiration"/>.
        /// </summary>
        public TimeSpan? AbsoluteExpiration { get; init; }

        /// <summary>
        /// Sliding time-to-live for the cached response (resets on each hit).
        /// Mutually exclusive with <see cref="AbsoluteExpiration"/>.
        /// </summary>
        public TimeSpan? SlidingExpiration { get; init; }

        /// <summary>
        /// Tag templates applied to every cache entry written by this rule.
        /// Each template is resolved against the request message before being attached
        /// (e.g. <c>"tenant:{tenantId}"</c> becomes <c>"tenant:t-7"</c>).
        /// Use these tags from <see cref="GrpcInvalidateRule.Tags"/> on a write method
        /// to flush all related entries at once.
        /// </summary>
        public string[] Tags { get; init; } = Array.Empty<string>();

        /// <summary>
        /// Whitelist of proto JSON field names (lowerCamelCase) that participate in the
        /// cache key. When empty, all scalar fields of the request are used.
        /// Useful to exclude bearer tokens or correlation IDs that would otherwise make
        /// every key unique.
        /// </summary>
        public string[] IncludeFields { get; init; } = Array.Empty<string>();
    }

    /// <summary>
    /// Describes a unary gRPC method that should evict cache entries when it succeeds.
    /// </summary>
    public sealed class GrpcInvalidateRule
    {
        /// <summary>
        /// Fully qualified gRPC method path, e.g. <c>/order.OrderService/UpdateOrder</c>.
        /// Must include the leading slash and match <c>Grpc.Core.Method.FullName</c>.
        /// </summary>
        public string FullMethod { get; init; } = string.Empty;

        /// <summary>
        /// Tag templates to invalidate. Resolved against the request message — e.g.
        /// <c>"tenant:{tenantId}"</c> evicts every entry tagged <c>"tenant:t-7"</c>.
        /// </summary>
        public string[] Tags { get; init; } = Array.Empty<string>();

        /// <summary>
        /// Glob-style key patterns to invalidate. Resolved against the request message — e.g.
        /// <c>"order:{tenantId}:*"</c> removes every order key for that tenant.
        /// </summary>
        public string[] KeyPatterns { get; init; } = Array.Empty<string>();

        /// <summary>
        /// When <see langword="true"/>, invalidation runs <em>before</em> the gRPC call.
        /// Default is <see langword="false"/> (after a successful call).
        /// </summary>
        public bool InvalidateBefore { get; init; } = false;
    }
}
