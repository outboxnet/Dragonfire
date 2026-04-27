namespace Dragonfire.TenantContext.Resolution;

/// <summary>
/// Result of a single <see cref="ITenantResolver"/> invocation.
/// </summary>
public readonly record struct TenantResolution
{
    /// <summary>An "unresolved" sentinel returned when a resolver cannot determine a tenant.</summary>
    public static TenantResolution Unresolved { get; } = default;

    public TenantResolution(TenantId tenantId, string source, IReadOnlyDictionary<string, string>? properties = null)
    {
        TenantId = tenantId;
        Source = source ?? string.Empty;
        Properties = properties;
    }

    public TenantId TenantId { get; }
    public string Source { get; }
    public IReadOnlyDictionary<string, string>? Properties { get; }

    public bool IsResolved => !TenantId.IsEmpty;

    /// <summary>Convenience factory — equivalent to constructing a successful resolution.</summary>
    public static TenantResolution Resolved(TenantId tenantId, string source, IReadOnlyDictionary<string, string>? properties = null)
        => new(tenantId, source, properties);
}
