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
    /// Server-side gRPC interceptor that logs every inbound call using
    /// <see cref="ILogger{T}.BeginScope"/> with individually named structured properties
    /// compatible with Application Insights <c>customDimensions</c>, Seq, Loki, and any
    /// other <c>ILogger</c>-compatible sink.
    ///
    /// <para><b>Registration</b></para>
    /// <code>
    /// // 1. Register options + interceptor in DI
    /// builder.Services.AddDragonfireGrpcServerLogging(options =>
    /// {
    ///     options.LogRequestFields  = true;
    ///     options.LogResponseFields = true;
    ///     options.ExcludeFields.Add("authToken");
    /// });
    ///
    /// // 2. Add to the gRPC server pipeline
    /// builder.Services.AddGrpc(options =>
    ///     options.Interceptors.Add&lt;DragonfireServerLoggingInterceptor&gt;());
    /// </code>
    ///
    /// <para><b>Call-type behaviour</b></para>
    /// <list type="table">
    ///   <listheader><term>Call type</term><description>Logged fields</description></listheader>
    ///   <item><term>Unary</term>
    ///         <description>Request + Response scalar fields; logs on response completion.</description></item>
    ///   <item><term>Client streaming</term>
    ///         <description>Response scalar fields only (no single request object); logs on response completion.</description></item>
    ///   <item><term>Server streaming</term>
    ///         <description>Request scalar fields only (no single response object); logs when response stream is exhausted.</description></item>
    ///   <item><term>Bidirectional streaming</term>
    ///         <description>No field extraction (both sides stream); logs when response stream is exhausted.</description></item>
    /// </list>
    ///
    /// <para><b>Field naming</b></para>
    /// Proto fields are referenced by their JSON name (lowerCamelCase), e.g.
    /// the proto field <c>tenant_id</c> becomes <c>Request.tenantId</c>.
    /// </summary>
    public sealed class DragonfireServerLoggingInterceptor : Interceptor
    {
        private readonly ILogger<DragonfireServerLoggingInterceptor> _logger;
        private readonly DragonfireGrpcServerOptions _options;

        public DragonfireServerLoggingInterceptor(
            ILogger<DragonfireServerLoggingInterceptor> logger,
            DragonfireGrpcServerOptions options)
        {
            _logger  = logger;
            _options = options;
        }

        // ── Unary ─────────────────────────────────────────────────────────────
        // One request in, one response out.
        // Both request and response field extraction are available.

        public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
            TRequest request,
            ServerCallContext context,
            UnaryServerMethod<TRequest, TResponse> continuation)
        {
            var (service, method) = ParseMethod(context.Method);
            var scope = BuildScope(context.Method, service, method, context.Peer);

            if (_options.LogRequestFields)
                GrpcFieldExtractor.Extract(request, "Request",
                    _options.ExcludeFields, _options.IncludeFields, scope);

            var sw = Stopwatch.StartNew();
            try
            {
                var response = await continuation(request, context).ConfigureAwait(false);
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
        // Client sends N request messages; server returns one response.
        // There is no single request object to extract fields from, but the
        // single response is available after the handler completes.

        public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
            IAsyncStreamReader<TRequest> requestStream,
            ServerCallContext context,
            ClientStreamingServerMethod<TRequest, TResponse> continuation)
        {
            var (service, method) = ParseMethod(context.Method);
            var scope = BuildScope(context.Method, service, method, context.Peer);
            scope["Dragonfire.GrpcCallType"] = "ClientStreaming";

            // Request fields are unavailable — messages are streamed one at a time.
            // Response field extraction happens below after the handler returns.

            var sw = Stopwatch.StartNew();
            try
            {
                var response = await continuation(requestStream, context).ConfigureAwait(false);
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

        // ── Server streaming ──────────────────────────────────────────────────
        // Client sends one request; server streams N response messages.
        // Request fields are available; response fields are not (stream of messages).
        // We wrap the IServerStreamWriter to detect when the stream is exhausted.
        // Logging fires when the handler Task completes (stream fully written).

        public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
            TRequest request,
            IServerStreamWriter<TResponse> responseStream,
            ServerCallContext context,
            ServerStreamingServerMethod<TRequest, TResponse> continuation)
        {
            var (service, method) = ParseMethod(context.Method);
            var scope = BuildScope(context.Method, service, method, context.Peer);
            scope["Dragonfire.GrpcCallType"] = "ServerStreaming";

            if (_options.LogRequestFields)
                GrpcFieldExtractor.Extract(request, "Request",
                    _options.ExcludeFields, _options.IncludeFields, scope);

            // Response fields cannot be captured (multiple messages).
            // Elapsed is measured across the full streaming duration.

            var sw = Stopwatch.StartNew();
            try
            {
                await continuation(request, responseStream, context).ConfigureAwait(false);
                sw.Stop();
                LogSuccess(scope, sw, service, method);
            }
            catch (Exception ex)
            {
                sw.Stop();
                LogError(scope, sw, service, method, ex);
                throw;
            }
        }

        // ── Bidirectional streaming ───────────────────────────────────────────
        // Both sides stream messages. No single request or response object exists.
        // We log elapsed across the full duplex session duration.

        public override async Task BidiStreamingServerHandler<TRequest, TResponse>(
            IAsyncStreamReader<TRequest> requestStream,
            IServerStreamWriter<TResponse> responseStream,
            ServerCallContext context,
            DuplexStreamingServerMethod<TRequest, TResponse> continuation)
        {
            var (service, method) = ParseMethod(context.Method);
            var scope = BuildScope(context.Method, service, method, context.Peer);
            scope["Dragonfire.GrpcCallType"] = "BidiStreaming";

            // Neither request nor response fields can be extracted from a duplex stream.

            var sw = Stopwatch.StartNew();
            try
            {
                await continuation(requestStream, responseStream, context).ConfigureAwait(false);
                sw.Stop();
                LogSuccess(scope, sw, service, method);
            }
            catch (Exception ex)
            {
                sw.Stop();
                LogError(scope, sw, service, method, ex);
                throw;
            }
        }

        // ── Scope / log helpers ───────────────────────────────────────────────

        private static DragonfireGrpcScopeState BuildScope(
            string fullMethod, string service, string method, string? peer)
        {
            var scope = new DragonfireGrpcScopeState($"[Dragonfire:gRPC] {service}.{method}");
            scope["Dragonfire.GrpcMethod"]     = fullMethod;
            scope["Dragonfire.GrpcService"]    = service;
            scope["Dragonfire.GrpcMethodName"] = method;
            if (!string.IsNullOrEmpty(peer))
                scope["Dragonfire.GrpcPeer"]   = peer;
            return scope;
        }

        private void LogSuccess(DragonfireGrpcScopeState scope, Stopwatch sw,
            string service, string method)
        {
            scope["Dragonfire.ElapsedMs"] = sw.Elapsed.TotalMilliseconds;
            using (_logger.BeginScope(scope))
                _logger.Log(
                    _options.LogLevel,
                    "[Dragonfire:gRPC] {Dragonfire_GrpcService}.{Dragonfire_GrpcMethodName} completed in {Dragonfire_ElapsedMs}ms",
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
                    "[Dragonfire:gRPC] {Dragonfire_GrpcService}.{Dragonfire_GrpcMethodName} FAILED in {Dragonfire_ElapsedMs}ms — {Dragonfire_ErrorMessage}",
                    service, method, sw.Elapsed.TotalMilliseconds,
                    scope["Dragonfire.ErrorMessage"]);
        }

        // ── Method name parsing ───────────────────────────────────────────────
        // gRPC method format: "/package.ServiceName/MethodName"
        // → service = "ServiceName", method = "MethodName"

        private static (string service, string method) ParseMethod(string fullMethod)
        {
            var trimmed = fullMethod.TrimStart('/');
            var slash   = trimmed.LastIndexOf('/');
            if (slash < 0) return (trimmed, trimmed);

            var serviceFullName = trimmed[..slash];     // "package.ServiceName"
            var methodName      = trimmed[(slash + 1)..]; // "MethodName"

            var dot         = serviceFullName.LastIndexOf('.');
            var serviceName = dot >= 0
                ? serviceFullName[(dot + 1)..]   // strip package prefix
                : serviceFullName;

            return (serviceName, methodName);
        }
    }
}
