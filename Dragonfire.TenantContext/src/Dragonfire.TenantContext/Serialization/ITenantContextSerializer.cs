namespace Dragonfire.TenantContext.Serialization;

/// <summary>
/// Serializes <see cref="TenantInfo"/> to/from a portable string representation, so that
/// outbox messages, queued background jobs, and saga state can carry the originating tenant
/// across process boundaries without leaking the resolution mechanism.
/// </summary>
public interface ITenantContextSerializer
{
    /// <summary>Stable content type identifier (e.g. <c>"application/x-tenant+json;v=1"</c>).</summary>
    string ContentType { get; }

    /// <summary>Serializes the tenant. Must be safe to call with <see cref="TenantInfo.None"/>.</summary>
    string Serialize(TenantInfo tenant);

    /// <summary>Deserializes a previously serialized payload. Returns <see cref="TenantInfo.None"/> for null/empty input.</summary>
    TenantInfo Deserialize(string? payload);
}
