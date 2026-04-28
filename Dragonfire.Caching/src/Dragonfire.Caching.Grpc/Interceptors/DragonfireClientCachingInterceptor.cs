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
    /// Client-side gRPC interceptor that caches outbound unary calls and runs invalidation
    /// for write-style methods. Streaming calls (client-streaming, server-streaming,
    /// bidirectional) pass through untouched — caching streamed payloads is not safe
    /// in a generic way.
    ///
    /// <para><b>Registration</b></para>
    /// <code>
    /// builder.Services.AddDragonfireGrpcClientCaching(options =>
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
    /// builder.Services.AddGrpcClient&lt;OrderService.OrderServiceClient&gt;(o =>
    ///         o.Address = new Uri("https://order-service:5001"))
    ///     .AddInterceptor&lt;DragonfireClientCachingInterceptor&gt;();
    /// </code>
    /// </summary>
    public sealed class DragonfireClientCachingInterceptor : Interceptor
    {
        private readonly ICacheService _cache;
        private readonly ICacheKeyStrategy _keyStrategy;
        private readonly DragonfireGrpcCachingClientOptions _options;

        public DragonfireClientCachingInterceptor(
            ICacheService cache,
            ICacheKeyStrategy keyStrategy,
            DragonfireGrpcCachingClientOptions options)
        {
            _cache       = cache;
            _keyStrategy = keyStrategy;
            _options     = options;
        }

        // ── Async unary ───────────────────────────────────────────────────────
        // The only call type that benefits from caching: a single request, a single
        // response. Streaming variants are pass-through.

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            TRequest request,
            ClientInterceptorContext<TRequest, TResponse> context,
            AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
        {
            var fullMethod = context.Method.FullName;

            if (_options.CacheRules.TryGetValue(fullMethod, out var cacheRule))
                return CachedAsyncUnary(request, context, continuation, cacheRule);

            if (_options.InvalidateRules.TryGetValue(fullMethod, out var invRule))
                return InvalidatingAsyncUnary(request, context, continuation, invRule);

            return continuation(request, context);
        }

        // Blocking variant — same logic, synchronous boundary.
        public override TResponse BlockingUnaryCall<TRequest, TResponse>(
            TRequest request,
            ClientInterceptorContext<TRequest, TResponse> context,
            BlockingUnaryCallContinuation<TRequest, TResponse> continuation)
        {
            var fullMethod = context.Method.FullName;

            if (_options.CacheRules.TryGetValue(fullMethod, out var cacheRule))
            {
                var (service, method) = GrpcCacheKeyBuilder.ParseFullMethod(fullMethod);
                var args = GrpcCacheKeyBuilder.Extract(request, cacheRule.IncludeFields);
                var key  = _keyStrategy.GenerateKey(service, method, args, cacheRule.KeyTemplate);

                // Sync-over-async at the proxy boundary — gRPC's blocking call is itself
                // a client thread block; matching that semantically is acceptable here.
                return _cache.GetOrAddAsync<TResponse>(
                        key,
                        () => Task.FromResult(continuation(request, context)),
                        opts => Configure(opts, cacheRule, args))
                    .GetAwaiter().GetResult();
            }

            if (_options.InvalidateRules.TryGetValue(fullMethod, out var invRule))
            {
                var args = GrpcCacheKeyBuilder.Extract(request, Array.Empty<string>());
                if (invRule.InvalidateBefore) RunInvalidation(invRule, args).GetAwaiter().GetResult();
                var response = continuation(request, context);
                if (!invRule.InvalidateBefore) RunInvalidation(invRule, args).GetAwaiter().GetResult();
                return response;
            }

            return continuation(request, context);
        }

        // ── AsyncUnary helpers ────────────────────────────────────────────────

        private AsyncUnaryCall<TResponse> CachedAsyncUnary<TRequest, TResponse>(
            TRequest request,
            ClientInterceptorContext<TRequest, TResponse> context,
            AsyncUnaryCallContinuation<TRequest, TResponse> continuation,
            GrpcCacheRule cacheRule)
            where TRequest  : class
            where TResponse : class
        {
            var (service, method) = GrpcCacheKeyBuilder.ParseFullMethod(context.Method.FullName);
            var args = GrpcCacheKeyBuilder.Extract(request, cacheRule.IncludeFields);
            var key  = _keyStrategy.GenerateKey(service, method, args, cacheRule.KeyTemplate);

            // Hold the underlying call handle (set on cache miss) so we can forward
            // headers/status/trailers/dispose if the inner call really executed.
            AsyncUnaryCall<TResponse>? inner = null;

            async Task<TResponse> ResolveAsync()
            {
                return await _cache.GetOrAddAsync<TResponse>(
                    key,
                    async () =>
                    {
                        inner = continuation(request, context);
                        return await inner.ResponseAsync.ConfigureAwait(false);
                    },
                    opts => Configure(opts, cacheRule, args)).ConfigureAwait(false);
            }

            return new AsyncUnaryCall<TResponse>(
                ResolveAsync(),
                inner is null ? Task.FromResult(new Metadata()) : inner.ResponseHeadersAsync,
                ()  => inner?.GetStatus()   ?? Status.DefaultSuccess,
                ()  => inner?.GetTrailers() ?? new Metadata(),
                ()  => inner?.Dispose());
        }

        private AsyncUnaryCall<TResponse> InvalidatingAsyncUnary<TRequest, TResponse>(
            TRequest request,
            ClientInterceptorContext<TRequest, TResponse> context,
            AsyncUnaryCallContinuation<TRequest, TResponse> continuation,
            GrpcInvalidateRule invRule)
            where TRequest  : class
            where TResponse : class
        {
            var args = GrpcCacheKeyBuilder.Extract(request, Array.Empty<string>());
            var inner = continuation(request, context);

            async Task<TResponse> CompleteAsync()
            {
                if (invRule.InvalidateBefore)
                    await RunInvalidation(invRule, args).ConfigureAwait(false);

                var response = await inner.ResponseAsync.ConfigureAwait(false);

                if (!invRule.InvalidateBefore)
                    await RunInvalidation(invRule, args).ConfigureAwait(false);

                return response;
            }

            return new AsyncUnaryCall<TResponse>(
                CompleteAsync(),
                inner.ResponseHeadersAsync,
                inner.GetStatus,
                inner.GetTrailers,
                inner.Dispose);
        }

        // ── Shared configuration / invalidation helpers ───────────────────────

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
