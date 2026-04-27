using Grpc.Core;
using Grpc.Core.Interceptors;
using TenantContext.Resolution;

namespace TenantContext.Grpc;

/// <summary>
/// gRPC server interceptor that runs the configured resolution pipeline against the incoming
/// call's metadata and pushes the resulting tenant onto the <see cref="ITenantContextSetter"/>
/// scope for the duration of the handler.
/// </summary>
public sealed class TenantServerInterceptor : Interceptor
{
    private readonly ITenantResolutionPipeline _pipeline;
    private readonly ITenantContextSetter _setter;

    public TenantServerInterceptor(ITenantResolutionPipeline pipeline, ITenantContextSetter setter)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _setter = setter ?? throw new ArgumentNullException(nameof(setter));
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        using var scope = await BeginScopeAsync(context).ConfigureAwait(false);
        return await continuation(request, context).ConfigureAwait(false);
    }

    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream, ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        using var scope = await BeginScopeAsync(context).ConfigureAwait(false);
        return await continuation(requestStream, context).ConfigureAwait(false);
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request, IServerStreamWriter<TResponse> responseStream, ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        using var scope = await BeginScopeAsync(context).ConfigureAwait(false);
        await continuation(request, responseStream, context).ConfigureAwait(false);
    }

    public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream, IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context, DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        using var scope = await BeginScopeAsync(context).ConfigureAwait(false);
        await continuation(requestStream, responseStream, context).ConfigureAwait(false);
    }

    private async ValueTask<IDisposable> BeginScopeAsync(ServerCallContext context)
    {
        var resolutionContext = new TenantResolutionContext { CancellationToken = context.CancellationToken }
            .With(TenantResolutionContext.GrpcServerCallContextKey, context);

        var tenant = await _pipeline.ResolveAsync(resolutionContext, context.CancellationToken).ConfigureAwait(false);
        return tenant.IsResolved ? _setter.BeginScope(tenant) : NoopScope.Instance;
    }

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();
        public void Dispose() { }
    }
}
