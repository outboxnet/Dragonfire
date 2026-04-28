using System.Collections.Generic;

namespace Dragonfire.Features;

/// <summary>
/// In-memory snapshot of the most recently loaded feature definitions. Updated by the refresh
/// hosted service, queried by <see cref="IFeatureResolver"/> implementations.
/// </summary>
public interface IFeatureStore
{
    /// <summary>Look up a single feature by name. Returns <c>null</c> when no rule exists.</summary>
    FeatureDefinition? Get(string name);

    /// <summary>Snapshot of all currently loaded definitions.</summary>
    IReadOnlyCollection<FeatureDefinition> GetAll();

    /// <summary>
    /// Replace the entire snapshot atomically. Returns the diff (added, updated, removed)
    /// so the refresh service can emit audit entries.
    /// </summary>
    FeatureStoreDiff Replace(IReadOnlyCollection<FeatureDefinition> definitions);
}

/// <summary>Result of a snapshot replacement — used to drive audit emission.</summary>
public sealed class FeatureStoreDiff
{
    public FeatureStoreDiff(
        IReadOnlyList<FeatureDefinition> added,
        IReadOnlyList<(FeatureDefinition Previous, FeatureDefinition Current)> updated,
        IReadOnlyList<FeatureDefinition> removed)
    {
        Added   = added;
        Updated = updated;
        Removed = removed;
    }

    public IReadOnlyList<FeatureDefinition> Added { get; }
    public IReadOnlyList<(FeatureDefinition Previous, FeatureDefinition Current)> Updated { get; }
    public IReadOnlyList<FeatureDefinition> Removed { get; }

    public bool HasChanges => Added.Count + Updated.Count + Removed.Count > 0;
}
