using System.Diagnostics.Metrics;

namespace Dragonfire.Caching.Models;

/// <summary>
/// OpenTelemetry-compatible metrics for cache operations.
/// Counters are exposed under the <c>Dragonfire.Caching</c> meter name.
/// </summary>
public sealed class CacheMetrics
{
    private readonly Counter<long> _hits;
    private readonly Counter<long> _misses;
    private readonly Counter<long> _sets;
    private readonly Counter<long> _removals;

    // Singleton meter — safe to share across instances.
    private static readonly Meter _meter = new("Dragonfire.Caching", "3.0.0");

    public CacheMetrics()
    {
        _hits     = _meter.CreateCounter<long>("dragonfire.cache.hits",     description: "Total cache hits");
        _misses   = _meter.CreateCounter<long>("dragonfire.cache.misses",   description: "Total cache misses");
        _sets     = _meter.CreateCounter<long>("dragonfire.cache.sets",     description: "Total cache sets");
        _removals = _meter.CreateCounter<long>("dragonfire.cache.removals", description: "Total cache removals");
    }

    public void RecordHit(string provider)     => _hits.Add(1,     new KeyValuePair<string, object?>("provider", provider));
    public void RecordMiss(string provider)    => _misses.Add(1,   new KeyValuePair<string, object?>("provider", provider));
    public void RecordSet(string provider)     => _sets.Add(1,     new KeyValuePair<string, object?>("provider", provider));
    public void RecordRemoval(string provider) => _removals.Add(1, new KeyValuePair<string, object?>("provider", provider));
}
