using System;
using System.Collections.Generic;

namespace Dragonfire.Features.EntityFrameworkCore;

/// <summary>EF-mapped row for a single feature definition.</summary>
public sealed class FeatureEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool DefaultEnabled { get; set; }
    public long Version { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<FeatureRuleEntity> Rules { get; set; } = new();
}

/// <summary>EF-mapped row for one rule attached to a feature.</summary>
public sealed class FeatureRuleEntity
{
    public long Id { get; set; }
    public string FeatureName { get; set; } = string.Empty;
    public int Order { get; set; }

    /// <summary>One of <c>tenant</c>, <c>user</c>, <c>percentage</c>.</summary>
    public string RuleType { get; set; } = string.Empty;

    /// <summary>JSON or comma-list payload — interpreted per <see cref="RuleType"/>.</summary>
    public string Payload { get; set; } = string.Empty;
}

/// <summary>EF-mapped row for one audit entry.</summary>
public sealed class FeatureAuditEntity
{
    public long Id { get; set; }
    public string FeatureName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public string Source { get; set; } = string.Empty;
    public long? PreviousVersion { get; set; }
    public long? CurrentVersion { get; set; }
    public string? MetadataJson { get; set; }
}
