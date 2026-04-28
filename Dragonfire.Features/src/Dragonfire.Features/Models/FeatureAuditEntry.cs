using System;
using System.Collections.Generic;

namespace Dragonfire.Features;

/// <summary>What kind of change the audit entry represents.</summary>
public enum FeatureAuditAction
{
    Added    = 0,
    Updated  = 1,
    Removed  = 2,
}

/// <summary>
/// Immutable record describing a single change to a feature definition. Persisted by
/// <see cref="IFeatureAuditLog"/> implementations for B2B compliance.
/// </summary>
public sealed class FeatureAuditEntry
{
    public FeatureAuditEntry(
        string featureName,
        FeatureAuditAction action,
        DateTimeOffset timestamp,
        string source,
        long? previousVersion,
        long? currentVersion,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        FeatureName     = featureName;
        Action          = action;
        Timestamp       = timestamp;
        Source          = source;
        PreviousVersion = previousVersion;
        CurrentVersion  = currentVersion;
        Metadata        = metadata ?? EmptyMetadata;
    }

    public string FeatureName { get; }
    public FeatureAuditAction Action { get; }
    public DateTimeOffset Timestamp { get; }

    /// <summary>Identifier of the source that triggered the change (e.g. <c>configuration</c>, <c>efcore</c>).</summary>
    public string Source { get; }

    public long? PreviousVersion { get; }
    public long? CurrentVersion { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }

    private static readonly IReadOnlyDictionary<string, string> EmptyMetadata
        = new Dictionary<string, string>(0);
}
