using System.Collections.Frozen;

namespace TenantContext;

/// <summary>
/// Immutable snapshot of the current tenant: identifier plus optional metadata
/// (display name, region, schema, custom claims) and provenance (which resolver produced it).
/// </summary>
public sealed class TenantInfo
{
    /// <summary>An empty tenant info, used when no tenant has been resolved.</summary>
    public static TenantInfo None { get; } = new(TenantId.Empty, source: "none", properties: null);

    public TenantInfo(
        TenantId tenantId,
        string? source = null,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        TenantId = tenantId;
        Source = source ?? string.Empty;
        Properties = properties is null or { Count: 0 }
            ? FrozenDictionary<string, string>.Empty
            : properties.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The resolved tenant id. May be <see cref="TenantId.Empty"/>.</summary>
    public TenantId TenantId { get; }

    /// <summary>True when a tenant id was resolved.</summary>
    public bool IsResolved => !TenantId.IsEmpty;

    /// <summary>Name of the resolver that produced this context (e.g. "header", "subdomain", "claim:tid").</summary>
    public string Source { get; }

    /// <summary>Additional metadata associated with the tenant. Keys are case-insensitive.</summary>
    public IReadOnlyDictionary<string, string> Properties { get; }

    /// <summary>Returns a copy with a different <see cref="Source"/>.</summary>
    public TenantInfo WithSource(string source)
        => new(TenantId, source, Properties);

    /// <summary>Returns a copy with merged properties (incoming wins on collision).</summary>
    public TenantInfo WithProperties(IReadOnlyDictionary<string, string>? properties)
    {
        if (properties is null || properties.Count == 0) return this;
        var merged = new Dictionary<string, string>(Properties, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in properties) merged[kv.Key] = kv.Value;
        return new TenantInfo(TenantId, Source, merged);
    }

    public override string ToString() => IsResolved ? $"{TenantId.Value} (via {Source})" : "<no tenant>";
}
