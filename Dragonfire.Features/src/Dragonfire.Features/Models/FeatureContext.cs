using System.Collections.Generic;

namespace Dragonfire.Features;

/// <summary>
/// Describes who is asking — used by <see cref="IFeatureResolver"/> to evaluate per-tenant,
/// per-user and percentage rules. Built by <see cref="IFeatureContextAccessor"/> from the
/// ambient request (e.g. tenant resolver + authenticated principal).
/// </summary>
public sealed class FeatureContext
{
    public static FeatureContext Empty { get; } = new(tenantId: null, userId: null);

    public FeatureContext(
        string? tenantId,
        string? userId,
        IReadOnlyDictionary<string, string>? attributes = null)
    {
        TenantId = tenantId;
        UserId = userId;
        Attributes = attributes ?? EmptyAttributes;
    }

    /// <summary>Resolved tenant id, or <c>null</c> when the call is anonymous / not multi-tenant.</summary>
    public string? TenantId { get; }

    /// <summary>Resolved user id (subject), or <c>null</c> when anonymous.</summary>
    public string? UserId { get; }

    /// <summary>Free-form attributes for custom rules (e.g. region, plan).</summary>
    public IReadOnlyDictionary<string, string> Attributes { get; }

    private static readonly IReadOnlyDictionary<string, string> EmptyAttributes
        = new Dictionary<string, string>(0);
}
