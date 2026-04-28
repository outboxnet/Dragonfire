using System;
using System.Collections.Generic;

namespace Dragonfire.Caching.Grpc.Configuration
{
    /// <summary>
    /// Runtime options for <see cref="Interceptors.DragonfireClientCachingInterceptor"/>.
    /// Configure once at DI registration time; the same instance is injected into every call.
    ///
    /// <para>
    /// Rules are keyed by the gRPC method's full name (e.g. <c>/order.OrderService/GetOrder</c>).
    /// Methods without a matching rule pass through unchanged.
    /// </para>
    /// </summary>
    public sealed class DragonfireGrpcCachingClientOptions
    {
        /// <summary>Per-method cache rules, keyed by <see cref="GrpcCacheRule.FullMethod"/>.</summary>
        public Dictionary<string, GrpcCacheRule> CacheRules { get; } =
            new Dictionary<string, GrpcCacheRule>(StringComparer.Ordinal);

        /// <summary>Per-method invalidation rules, keyed by <see cref="GrpcInvalidateRule.FullMethod"/>.</summary>
        public Dictionary<string, GrpcInvalidateRule> InvalidateRules { get; } =
            new Dictionary<string, GrpcInvalidateRule>(StringComparer.Ordinal);

        /// <summary>Register a cache rule. Replaces any existing rule with the same <see cref="GrpcCacheRule.FullMethod"/>.</summary>
        public DragonfireGrpcCachingClientOptions Cache(GrpcCacheRule rule)
        {
            if (rule is null) throw new ArgumentNullException(nameof(rule));
            if (string.IsNullOrEmpty(rule.FullMethod))
                throw new ArgumentException("FullMethod must be set.", nameof(rule));
            CacheRules[rule.FullMethod] = rule;
            return this;
        }

        /// <summary>Register an invalidation rule. Replaces any existing rule with the same <see cref="GrpcInvalidateRule.FullMethod"/>.</summary>
        public DragonfireGrpcCachingClientOptions Invalidate(GrpcInvalidateRule rule)
        {
            if (rule is null) throw new ArgumentNullException(nameof(rule));
            if (string.IsNullOrEmpty(rule.FullMethod))
                throw new ArgumentException("FullMethod must be set.", nameof(rule));
            InvalidateRules[rule.FullMethod] = rule;
            return this;
        }
    }
}
