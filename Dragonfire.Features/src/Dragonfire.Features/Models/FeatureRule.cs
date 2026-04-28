using System;
using System.Collections.Generic;
using System.Globalization;

namespace Dragonfire.Features;

/// <summary>
/// Polymorphic rule attached to a <see cref="FeatureDefinition"/>. The first rule that
/// returns a non-null verdict wins; if all rules abstain, the definition's
/// <see cref="FeatureDefinition.DefaultEnabled"/> is used.
/// </summary>
public abstract class FeatureRule
{
    /// <summary>
    /// Evaluate the rule for the given context. Return <c>true</c> to grant, <c>false</c> to deny,
    /// or <c>null</c> to abstain (let the next rule decide).
    /// </summary>
    public abstract bool? Evaluate(FeatureContext context);
}

/// <summary>
/// Allow-list of tenant ids. Returns <c>true</c> when the context tenant is in the list,
/// otherwise abstains.
/// </summary>
public sealed class TenantAllowListRule : FeatureRule
{
    public TenantAllowListRule(IEnumerable<string> tenantIds)
    {
        if (tenantIds is null) throw new ArgumentNullException(nameof(tenantIds));
        TenantIds = new HashSet<string>(tenantIds, StringComparer.OrdinalIgnoreCase);
    }

    public ISet<string> TenantIds { get; }

    public override bool? Evaluate(FeatureContext context)
    {
        if (string.IsNullOrEmpty(context.TenantId)) return null;
        return TenantIds.Contains(context.TenantId) ? true : null;
    }
}

/// <summary>
/// Allow-list of user ids — most useful for early-access beta cohorts and dogfooding.
/// </summary>
public sealed class UserAllowListRule : FeatureRule
{
    public UserAllowListRule(IEnumerable<string> userIds)
    {
        if (userIds is null) throw new ArgumentNullException(nameof(userIds));
        UserIds = new HashSet<string>(userIds, StringComparer.OrdinalIgnoreCase);
    }

    public ISet<string> UserIds { get; }

    public override bool? Evaluate(FeatureContext context)
    {
        if (string.IsNullOrEmpty(context.UserId)) return null;
        return UserIds.Contains(context.UserId) ? true : null;
    }
}

/// <summary>
/// Stable percentage rollout: a deterministic hash of (feature name + bucket key) maps the
/// caller into a 0-99 bucket, and the rule grants when bucket &lt; <see cref="Percentage"/>.
/// The bucket key prefers tenant id, then user id; if both are absent the rule abstains
/// (so an anonymous caller doesn't randomly flip on/off across requests).
/// </summary>
public sealed class PercentageRule : FeatureRule
{
    public PercentageRule(string featureName, int percentage, PercentageBucket bucket = PercentageBucket.TenantThenUser)
    {
        if (string.IsNullOrWhiteSpace(featureName))
            throw new ArgumentException("Feature name required.", nameof(featureName));
        if (percentage is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(percentage), "Must be between 0 and 100.");

        FeatureName = featureName;
        Percentage = percentage;
        Bucket = bucket;
    }

    public string FeatureName { get; }
    public int Percentage { get; }
    public PercentageBucket Bucket { get; }

    public override bool? Evaluate(FeatureContext context)
    {
        var key = Bucket switch
        {
            PercentageBucket.Tenant         => context.TenantId,
            PercentageBucket.User           => context.UserId,
            PercentageBucket.TenantThenUser => context.TenantId ?? context.UserId,
            _                               => null
        };

        if (string.IsNullOrEmpty(key)) return null;

        // FNV-1a 32-bit — stable across processes and platforms, no allocation.
        const uint offset = 2166136261;
        const uint prime  = 16777619;
        uint hash = offset;
        var seed = FeatureName + ":" + key;
        for (int i = 0; i < seed.Length; i++)
        {
            hash ^= seed[i];
            hash *= prime;
        }

        var bucket = (int)(hash % 100u);
        return bucket < Percentage ? true : null;
    }

    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"PercentageRule({FeatureName}, {Percentage}%, {Bucket})");
}

public enum PercentageBucket
{
    TenantThenUser = 0,
    Tenant         = 1,
    User           = 2,
}
