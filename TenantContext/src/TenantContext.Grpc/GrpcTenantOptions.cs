namespace TenantContext.Grpc;

/// <summary>Shared options for gRPC tenant propagation.</summary>
public sealed class GrpcTenantOptions
{
    /// <summary>
    /// gRPC metadata key carrying the tenant id. Must be lower-case and end with neither
    /// <c>-bin</c> (we send text) nor any reserved prefix. Default: <c>x-tenant-id</c>.
    /// </summary>
    public string MetadataKey { get; set; } = "x-tenant-id";

    /// <summary>Behavior on the client when there is no ambient tenant. Default: <c>Skip</c>.</summary>
    public ClientMissingBehavior ClientOnMissing { get; set; } = ClientMissingBehavior.Skip;
}

public enum ClientMissingBehavior
{
    Skip = 0,
    Throw = 1,
}
