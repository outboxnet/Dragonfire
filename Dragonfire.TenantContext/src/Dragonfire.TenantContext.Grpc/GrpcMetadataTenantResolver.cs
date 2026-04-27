using Grpc.Core;
using Microsoft.Extensions.Options;
using Dragonfire.TenantContext.Resolution;

namespace Dragonfire.TenantContext.Grpc;

/// <summary>
/// Server-side resolver that reads the tenant id from gRPC call metadata. Pair with
/// <see cref="TenantServerInterceptor"/>, which populates the <see cref="TenantResolutionContext"/>
/// before the resolution pipeline runs.
/// </summary>
public sealed class GrpcMetadataTenantResolver : ITenantResolver
{
    private readonly GrpcTenantOptions _options;

    public GrpcMetadataTenantResolver(IOptions<GrpcTenantOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value ?? new GrpcTenantOptions();
    }

    public string Name => $"grpc:{_options.MetadataKey}";

    public ValueTask<TenantResolution> ResolveAsync(TenantResolutionContext context, CancellationToken cancellationToken)
    {
        var call = context.Get<ServerCallContext>(TenantResolutionContext.GrpcServerCallContextKey);
        if (call is null) return ValueTask.FromResult(TenantResolution.Unresolved);

        foreach (var entry in call.RequestHeaders)
        {
            if (!entry.IsBinary && string.Equals(entry.Key, _options.MetadataKey, StringComparison.OrdinalIgnoreCase))
            {
                if (TenantId.TryParse(entry.Value, out var id))
                    return ValueTask.FromResult(TenantResolution.Resolved(id, Name));
                break;
            }
        }
        return ValueTask.FromResult(TenantResolution.Unresolved);
    }
}
