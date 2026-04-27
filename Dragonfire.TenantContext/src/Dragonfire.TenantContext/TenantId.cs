using System.Diagnostics.CodeAnalysis;

namespace Dragonfire.TenantContext;

/// <summary>
/// Strongly-typed tenant identifier. Empty value represents "no tenant resolved".
/// Equality is ordinal and case-sensitive by default; configure case-insensitive comparison via
/// <c>TenantResolutionOptions.TenantIdComparer</c> on the resolution pipeline.
/// </summary>
public readonly record struct TenantId
{
    /// <summary>The empty / unresolved tenant id.</summary>
    public static TenantId Empty => default;

    /// <summary>Creates a tenant id from a non-null, non-whitespace string. Whitespace is trimmed.</summary>
    public TenantId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim();
    }

    /// <summary>Raw tenant id value. Returns empty string for <see cref="Empty"/>.</summary>
    public string Value { get; } = string.Empty;

    /// <summary>True when this is the empty / unresolved tenant id.</summary>
    [MemberNotNullWhen(false, nameof(Value))]
    public bool IsEmpty => string.IsNullOrEmpty(Value);

    /// <summary>Permissive parser: returns <see cref="Empty"/> for null/whitespace input instead of throwing.</summary>
    public static TenantId From(string? value)
        => string.IsNullOrWhiteSpace(value) ? Empty : new TenantId(value);

    /// <summary>Tries to parse a tenant id without throwing.</summary>
    public static bool TryParse(string? value, out TenantId tenantId)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            tenantId = Empty;
            return false;
        }
        tenantId = new TenantId(value);
        return true;
    }

    public override string ToString() => Value;

    public static implicit operator string(TenantId id) => id.Value;
}
