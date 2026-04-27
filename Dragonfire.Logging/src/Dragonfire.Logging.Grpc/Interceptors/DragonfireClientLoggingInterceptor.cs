using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Dragonfire.Logging.Grpc.Configuration;
using Dragonfire.Logging.Grpc.Internal;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;

namespace Dragonfire.Logging.Grpc.Interceptors
{
    /// <summary>
    /// Client-side gRPC interceptor that logs every outbound call using
    /// <see cref="ILogger{T}.BeginScope"/> with individually named structured properties.
    ///
    /// <para><b>Registration</b></para>
    /// <code>
    /// // 1. Register options + interceptor in DI
    /// builder.Services.AddDragonfireGrpcClientLogging(options =>
    /// {
    ///     options.LogRequestFields  = true;
    ///     options.LogResponseFields = true;
    ///     options.ExcludeFields.Add("authToken");
    /// });
    ///
    /// // 2. Add to a gRPC client factory pipeline
    /// builder.Services.AddGrpcClient&lt;GreeterClient&gt;(o => o.Address = new Uri("https://..."))
    ///     .AddInterceptor&lt;DragonfireClientLoggingInterceptor&gt;();
    /// </code>
    ///
    /// <para><b>Call-type behaviour</b></para>
    /// <list type="table">
    ///   <listheader><term>Call type</term><description>Logged fields / timing</description></listheader>
    ///   <item><term>Async unary</term>
    ///         <description>Request + Response scalar fields; logs when response task completes.</description></item>
    ///   <item><term>Blocking unary</term>
    ///         <description>Request + Response scalar fields; logs when the blocking call returns.</description></item>
    ///   <item><term>Client streaming</term>
    ///         <description>Response scalar fields only; logs when ResponseAsync completes.</description></item>
    ///   <item><term>Server streaming</term>
    ///         <description>Request scalar fields only; logs when the response stream is fully consumed (MoveNext returns false).</description></item>
    ///   <item><term>Bidirectional streaming</term>
    ///         <description>No field extraction; logs when the response stream is fully consumed.</description></item>
    /// </list>
    /// </summary>
    public sealed class DragonfireClientLoggingInterceptor : Interceptor
    {
        private readonly ILogger<DragonfireClientLoggingInterceptor> _logger;
        private readonly DragonfireGrpcClientOptions _options;

        public DragonfireClientLoggingInterceptor(
            ILogger<DragonfireClientLoggingInterceptor> logger,
            DragonfireGrpcClientOptions options)
        {
            _logger  = logger;
            _options = options;
        }

        // ── Async unary ───────────────────────────────────────────────────────
        // Wraps ResponseAsync so we log after the response arrives.
        // This is the most common gRPC call pattern.

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            TRequest request,
            ClientInterceptorContext<TRequest, TResponse> context,
            AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
        {
            var (service, method) = ParseMethod(context.Method.FullName);
            var scope = BuildScope(context.Method.FullName, service, method, context.Host);

            if (_options.LogRequestFields)
                GrpcFieldExtractor.Extract(request, "Request",
                    _options.ExcludeFields, _options.IncludeFields, scope);

            var sw   = Stopwatch.StartNew();
            var call = continuation(request, context);

            async Task<TResponse> CompleteAsync()
            {
                try
                {
                    var response = await call.ResponseAsync.ConfigureAwait(false);
                    sw.Stop();

                    if (_options.LogResponseFields)
                        GrpcFieldExtractor.Extract(response, "Response",
                            _options.ExcludeFields, _options.IncludeFields, scope);

                    LogSuccess(scope, sw, service, method);
                    return response;
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    LogError(scope, sw, service, method, ex);
                    throw;
                }
            }

            return new AsyncUnaryCall<TResponse>(
                CompleteAsync(),
                call.ResponseHeadersAsync,
                call.GetStatus,
                call.GetTrailers,
                call.Dispose);
        }

        // ── Blocking unary ────────────────────────────────────────────────────
        // Synchronous path — less common but still supported.

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(
            TRequest request,
            ClientInterceptorContext<TRequest, TResponse> context,
            BlockingUnaryCallContinuation<TRequest, TResponse> continuation)
        {
            var (service, method) = ParseMethod(context.Method.FullName);
            var scope = BuildScope(context.Method.FullName, service, method, context.Host);

            if (_options.LogRequestFields)
                GrpcFieldExtractor.Extract(request, "Request",
                    _options.ExcludeFields, _options.IncludeFields, scope);

            var sw = Stopwatch.StartNew();
            try
            {
                var response = continuation(request, context);
                sw.Stop();

                if (_options.LogResponseFields)
                    GrpcFieldExtractor.Extract(response, "Response",
                        _options.ExcludeFields, _options.IncludeFields, scope);

                LogSuccess(scope, sw, service, method);
                return response;
            }
            catch (Exception ex)
            {
                sw.Stop();
                LogError(scope, sw, service, method, ex);
                throw;
            }
        }

        // ── Client streaming ──────────────────────────────────────────────────
        // Client streams N request messages; server returns one response.
        // No single request object is available. We hook into ResponseAsync
        // which completes after the client signals it has finished writing.

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
            ClientInterceptorContext<TRequest, TResponse> context,
            AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation)
        {
            var (service, method) = ParseMethod(context.Method.FullName);
            var scope = BuildScope(context.Method.FullName, service, method, context.Host);
            scope["Dragonfire.GrpcCallType"] = "ClientStreaming";

            var sw   = Stopwatch.StartNew();
            var call = continuation(context);

            async Task<TResponse> CompleteAsync()
            {
                try
                {
                    var response = await call.ResponseAsync.ConfigureAwait(false);
                    sw.Stop();

                    if (_options.LogResponseFields)
                        GrpcFieldExtractor.Extract(response, "Response",
                            _options.ExcludeFields, _options.IncludeFields, scope);

                    LogSuccess(scope, sw, service, method);
                    return response;
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    LogError(scope, sw, service, method, ex);
                    throw;
                }
            }

            return new AsyncClientStreamingCall<TRequest, TResponse>(
                call.RequestStream,
                CompleteAsync(),
                call.ResponseHeadersAsync,
                call.GetStatus,
                call.GetTrailers,
                call.Dispose);
        }

        // ── Server streaming ──────────────────────────────────────────────────
        // Client sends one request; server streams N response messages.
        // We extract request fields immediately, then wrap the response IAsyncStreamReader
        // so that logging fires when the stream is fully consumed (MoveNext → false).

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            TRequest request,
            ClientInterceptorContext<TRequest, TResponse> context,
            AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
        {
            var (service, method) = ParseMethod(context.Method.FullName);
            var scope = BuildScope(context.Method.FullName, service, method, context.Host);
            scope["Dragonfire.GrpcCallType"] = "ServerStreaming";

            if (_options.LogRequestFields)
                GrpcFieldExtractor.Extract(request, "Request",
                    _options.ExcludeFields, _options.IncludeFields, scope);

            var sw   = Stopwatch.StartNew();
            var call = continuation(request, context);

            // Wrap the stream reader: log fires when the consumer exhausts the stream.
            var wrappedStream = new LoggingStreamReader<TResponse>(
                call.ResponseStream,
                onComplete: () => { sw.Stop(); LogSuccess(scope, sw, service, method); },
                onError:    ex  => { sw.Stop(); LogError(scope, sw, service, method, ex); });

            return new AsyncServerStreamingCall<TResponse>(
                wrappedStream,
                call.ResponseHeadersAsync,
                call.GetStatus,
                call.GetTrailers,
                call.Dispose);
        }

        // ── Bidirectional streaming ───────────────────────────────────────────
        // Both sides stream messages. No single request or response to extract fields from.
        // We wrap the response stream reader — logging fires when the server closes the stream.

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
            ClientInterceptorContext<TRequest, TResponse> context,
            AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation)
        {
            var (service, method) = ParseMethod(context.Method.FullName);
            var scope = BuildScope(context.Method.FullName, service, method, context.Host);
            scope["Dragonfire.GrpcCallType"] = "BidiStreaming";

            var sw   = Stopwatch.StartNew();
            var call = continuation(context);

            var wrappedStream = new LoggingStreamReader<TResponse>(
                call.ResponseStream,
                onComplete: () => { sw.Stop(); LogSuccess(scope, sw, service, method); },
                onError:    ex  => { sw.Stop(); LogError(scope, sw, service, method, ex); });

            return new AsyncDuplexStreamingCall<TRequest, TResponse>(
                call.RequestStream,
                wrappedStream,
                call.ResponseHeadersAsync,
                call.GetStatus,
                call.GetTrailers,
                call.Dispose);
        }

        // ── Scope / log helpers ───────────────────────────────────────────────

        private static DragonfireGrpcScopeState BuildScope(
            string fullMethod, string service, string method, string? host)
        {
            var scope = new DragonfireGrpcScopeState($"[Dragonfire:gRPC Client] {service}.{method}");
            scope["Dragonfire.GrpcMethod"]     = fullMethod;
            scope["Dragonfire.GrpcService"]    = service;
            scope["Dragonfire.GrpcMethodName"] = method;
            if (!string.IsNullOrEmpty(host))
                scope["Dragonfire.GrpcTarget"] = host;
            return scope;
        }

        private void LogSuccess(DragonfireGrpcScopeState scope, Stopwatch sw,
            string service, string method)
        {
            scope["Dragonfire.ElapsedMs"] = sw.Elapsed.TotalMilliseconds;
            using (_logger.BeginScope(scope))
                _logger.Log(
                    _options.LogLevel,
                    "[Dragonfire:gRPC Client] {Dragonfire_GrpcService}.{Dragonfire_GrpcMethodName} completed in {Dragonfire_ElapsedMs}ms",
                    service, method, sw.Elapsed.TotalMilliseconds);
        }

        private void LogError(DragonfireGrpcScopeState scope, Stopwatch sw,
            string service, string method, Exception ex)
        {
            scope["Dragonfire.ElapsedMs"]    = sw.Elapsed.TotalMilliseconds;
            scope["Dragonfire.IsError"]      = true;
            scope["Dragonfire.GrpcStatus"]   = ex is RpcException rpc
                ? rpc.Status.StatusCode.ToString()
                : "Unknown";
            scope["Dragonfire.ErrorMessage"] = ex is RpcException rpcEx
                ? rpcEx.Status.Detail
                : ex.Message;
            if (_options.LogStackTrace)
                scope["Dragonfire.StackTrace"] = ex.StackTrace;

            using (_logger.BeginScope(scope))
                _logger.LogError(
                    ex,
                    "[Dragonfire:gRPC Client] {Dragonfire_GrpcService}.{Dragonfire_GrpcMethodName} FAILED in {Dragonfire_ElapsedMs}ms — {Dragonfire_ErrorMessage}",
                    service, method, sw.Elapsed.TotalMilliseconds,
                    scope["Dragonfire.ErrorMessage"]);
        }

        // ── Method name parsing ───────────────────────────────────────────────

        private static (string service, string method) ParseMethod(string fullMethod)
        {
            var trimmed = fullMethod.TrimStart('/');
            var slash   = trimmed.LastIndexOf('/');
            if (slash < 0) return (trimmed, trimmed);

            var serviceFullName = trimmed[..slash];
            var methodName      = trimmed[(slash + 1)..];

            var dot         = serviceFullName.LastIndexOf('.');
            var serviceName = dot >= 0
                ? serviceFullName[(dot + 1)..]
                : serviceFullName;

            return (serviceName, methodName);
        }
    }
}
