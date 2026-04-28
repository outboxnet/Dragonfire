using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

namespace Dragonfire.Features.Internal;

/// <summary>
/// Default <see cref="IFeatureStore"/>: holds an immutable dictionary keyed by feature name and
/// swaps it atomically on every refresh. Reads are lock-free.
/// </summary>
public sealed class InMemoryFeatureStore : IFeatureStore
{
    private ImmutableDictionary<string, FeatureDefinition> _snapshot
        = ImmutableDictionary.Create<string, FeatureDefinition>(StringComparer.OrdinalIgnoreCase);

    public FeatureDefinition? Get(string name)
        => _snapshot.TryGetValue(name, out var def) ? def : null;

    public IReadOnlyCollection<FeatureDefinition> GetAll()
    {
        var snapshot = Volatile.Read(ref _snapshot);
        var list = new List<FeatureDefinition>(snapshot.Count);
        foreach (var def in snapshot.Values) list.Add(def);
        return list;
    }

    public FeatureStoreDiff Replace(IReadOnlyCollection<FeatureDefinition> definitions)
    {
        if (definitions is null) throw new ArgumentNullException(nameof(definitions));

        var next = ImmutableDictionary.CreateBuilder<string, FeatureDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var def in definitions)
            next[def.Name] = def;
        var nextSnapshot = next.ToImmutable();

        // Diff against the previous snapshot before we publish.
        var previous = Volatile.Read(ref _snapshot);

        var added   = new List<FeatureDefinition>();
        var updated = new List<(FeatureDefinition Previous, FeatureDefinition Current)>();
        var removed = new List<FeatureDefinition>();

        foreach (var (name, def) in nextSnapshot)
        {
            if (!previous.TryGetValue(name, out var prev))
                added.Add(def);
            else if (prev.Version != def.Version)
                updated.Add((prev, def));
        }

        foreach (var (name, def) in previous)
            if (!nextSnapshot.ContainsKey(name))
                removed.Add(def);

        Volatile.Write(ref _snapshot, nextSnapshot);

        return new FeatureStoreDiff(added, updated, removed);
    }
}
