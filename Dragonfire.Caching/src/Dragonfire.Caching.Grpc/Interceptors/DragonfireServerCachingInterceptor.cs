using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dragonfire.Caching.Grpc.Configuration;
using Dragonfire.Caching.Grpc.Internal;
using Dragonfire.Caching.Interfaces;
using Dragonfire.Caching.Models;
using Dragonfire.Caching.Strategies;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Dragonfire.Caching.Grpc.Interceptors
{
    /// <summary>
    /// Server-side gRPC interceptor that short-circuits inbound unary handlers with cached
    /// responses, and runs invalidation for write-style methods. Streaming handlers
    /// (client-streaming, server-streaming, duplex) pass through untouched.
    ///
    /// <para><b>Registration</b></para>
    /// <code>
    /// builder.Services.AddDragonfireGrpcServerCaching(options =>
    /// {
    ///     options.Cache(new GrpcCacheRule {
    ///         FullMethod        = "/order.OrderService/GetOrder",
    ///         KeyTemplate       = "order:{tenantId}:{orderId}",
    ///         SlidingExpiration = TimeSpan.FromMinutes(5),
    ///         Tags              = new[] { "tenant:{tenantId}" }
    ///     });
    ///     options.Invalidate(new GrpcInvalidateRule {
    ///         FullMethod  = "/order.OrderService/UpdateOrder",
    ///         KeyPatterns = new[] { "order:{tenantId}:*" }
    ///     });
    /// });
    ///
    /// builder.Services.AddGrpc(o =>
    ///     o.Interceptors.Add&lt;DragonfireServerCachingInterceptor&gt;());
    /// </code>
    /// </summary>
    public sealed class DragonfireServerCachingInterceptor : Interceptor
    {
        private readonly ICacheService _cache;
        private readonly ICacheKeyStrategy _keyStrategy;
        private readonly DragonfireGrpcCachingServerOptions _options;

        public DragonfireServerCachingInterceptor(
            ICacheService cache,
            ICacheKeyStrategy keyStrategy,
            DragonfireGrpcCachingServerOptions options)
        {
            _cache       = cache;
            _keyStrategy = keyStrategy;
            _options     = options;
        }

        // ── Unary server handler ──────────────────────────────────────────────
        // ServerCallContext.Method matches Grpc.Core.Method.FullName on the client side,
        // so the same rule key works on both ends.

        public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
            TRequest request,
            ServerCallContext context,
            UnaryServerMethod<TRequest, TResponse> continuation)
        {
            var fullMethod = context.Method;

            if (_options.CacheRules.TryGetValue(fullMethod, out var cacheRule))
            {
                var (service, method) = GrpcCacheKeyBuilder.ParseFullMethod(fullMethod);
                var args = GrpcCacheKeyBuilder.Extract(request, cacheRule.IncludeFields);
                var key  = _keyStrategy.GenerateKey(service, method, args, cacheRule.KeyTemplate);

                return await _cache.GetOrAddAsync<TResponse>(
                    key,
                    () => continuation(request, context),
                    opts => Configure(opts, cacheRule, args)).ConfigureAwait(false);
            }

            if (_options.InvalidateRules.TryGetValue(fullMethod, out var invRule))
            {
                var args = GrpcCacheKeyBuilder.Extract(request, Array.Empty<string>());

                if (invRule.InvalidateBefore)
                    await RunInvalidation(invRule, args).ConfigureAwait(false);

                var response = await continuation(request, context).ConfigureAwait(false);

                if (!invRule.InvalidateBefore)
                    await RunInvalidation(invRule, args).ConfigureAwait(false);

                return response;
            }

            return await continuation(request, context).ConfigureAwait(false);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void Configure(
            CacheEntryOptions opts,
            GrpcCacheRule rule,
            IReadOnlyDictionary<string, object?> args)
        {
            if (rule.AbsoluteExpiration is { } abs)
                opts.AbsoluteExpirationRelativeToNow = abs;
            else if (rule.SlidingExpiration is { } sl)
                opts.SlidingExpiration = sl;

            foreach (var tagTemplate in rule.Tags)
                opts.Tags.Add(_keyStrategy.GeneratePattern(tagTemplate, args));
        }

        private async Task RunInvalidation(
            GrpcInvalidateRule rule,
            IReadOnlyDictionary<string, object?> args)
        {
            foreach (var tagTemplate in rule.Tags)
            {
                var tag = _keyStrategy.GeneratePattern(tagTemplate, args);
                await _cache.InvalidateByTagAsync(tag).ConfigureAwait(false);
            }

            foreach (var patternTemplate in rule.KeyPatterns)
            {
                var pattern = _keyStrategy.GeneratePattern(patternTemplate, args);
                await _cache.RemoveByPatternAsync(pattern).ConfigureAwait(false);
            }
        }
    }
}
