using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Options;

namespace Dragonfire.TenantContext.Grpc;

/// <summary>
/// gRPC client interceptor that adds the ambient tenant id to outgoing call metadata.
/// Register on the channel/client builder.
/// </summary>
public sealed class TenantClientInterceptor : Interceptor
{
    private readonly ITenantContextAccessor _accessor;
    private readonly GrpcTenantOptions _options;

    public TenantClientInterceptor(ITenantContextAccessor accessor, IOptions<GrpcTenantOptions> options)
    {
        _accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value ?? new GrpcTenantOptions();
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request, ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
        => continuation(request, AddHeader(context));

    public override TResponse BlockingUnaryCall<TRequest, TResponse>(
        TRequest request, ClientInterceptorContext<TRequest, TResponse> context,
        BlockingUnaryCallContinuation<TRequest, TResponse> continuation)
        => continuation(request, AddHeader(context));

    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation)
        => continuation(AddHeader(context));

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request, ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
        => continuation(request, AddHeader(context));

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation)
        => continuation(AddHeader(context));

    private ClientInterceptorContext<TRequest, TResponse> AddHeader<TRequest, TResponse>(ClientInterceptorContext<TRequest, TResponse> context)
        where TRequest : class where TResponse : class
    {
        var tenant = _accessor.Current;
        if (!tenant.IsResolved)
        {
            if (_options.ClientOnMissing == ClientMissingBehavior.Throw)
                throw new InvalidOperationException("No ambient tenant; cannot propagate to outbound gRPC call.");
            return context;
        }

        var headers = context.Options.Headers ?? new Metadata();
        // Replace any existing entry to avoid duplicates.
        for (var i = headers.Count - 1; i >= 0; i--)
        {
            if (string.Equals(headers[i].Key, _options.MetadataKey, StringComparison.OrdinalIgnoreCase))
                headers.RemoveAt(i);
        }
        headers.Add(_options.MetadataKey, tenant.TenantId.Value);

        var newOptions = context.Options.WithHeaders(headers);
        return new ClientInterceptorContext<TRequest, TResponse>(context.Method, context.Host, newOptions);
    }
}
