namespace Dragonfire.Outbox.Options;

public class ProcessorOptions
{
    /// <summary>
    /// How long the cold-path background loop waits between DB scans.
    /// The cold path handles: messages published by other instances, scheduled retries,
    /// and recovery from channel overflow.
    /// Default: 1 second. Lower values improve cross-instance latency at the cost of
    /// slightly higher DB query rate (one lightweight indexed scan per interval per instance).
    /// </summary>
    public TimeSpan ColdPollingInterval { get; set; } = TimeSpan.FromSeconds(1);
}
