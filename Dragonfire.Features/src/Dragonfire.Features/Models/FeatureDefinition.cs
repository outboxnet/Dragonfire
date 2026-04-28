using System;
using System.Collections.Generic;
using System.Linq;

namespace Dragonfire.Features;

/// <summary>
/// One feature flag plus its rules. Rules are evaluated in order; the first non-null verdict
/// wins. If all rules abstain, <see cref="DefaultEnabled"/> is the answer.
/// </summary>
public sealed class FeatureDefinition
{
    public FeatureDefinition(
        string name,
        bool defaultEnabled = false,
        IEnumerable<FeatureRule>? rules = null,
        string? description = null,
        long version = 0)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Feature name required.", nameof(name));

        Name = name;
        DefaultEnabled = defaultEnabled;
        Rules = rules?.ToArray() ?? Array.Empty<FeatureRule>();
        Description = description;
        Version = version;
    }

    public string Name { get; }
    public bool DefaultEnabled { get; }
    public IReadOnlyList<FeatureRule> Rules { get; }
    public string? Description { get; }

    /// <summary>
    /// Monotonic version stamp from the source. Used by the audit log to detect "same content,
    /// new revision" updates and by callers that want optimistic concurrency.
    /// </summary>
    public long Version { get; }
}
