namespace Dragonfire.Caching.Models;

/// <summary>Runtime statistics exposed by <see cref="Interfaces.ICacheProvider"/>.</summary>
public sealed class CacheStatistics
{
    public long TotalHits;
    public long TotalMisses;
    public long TotalSets;
    public long TotalRemovals;

    public double HitRatio =>
        TotalHits + TotalMisses > 0
            ? (double)TotalHits / (TotalHits + TotalMisses)
            : 0;

    public long CurrentEntryCount { get; set; }
    public long EstimatedSizeBytes { get; set; }
    public DateTime LastReset { get; set; } = DateTime.UtcNow;

    public void Reset()
    {
        TotalHits = 0;
        TotalMisses = 0;
        TotalSets = 0;
        TotalRemovals = 0;
        CurrentEntryCount = 0;
        EstimatedSizeBytes = 0;
        LastReset = DateTime.UtcNow;
    }
}
