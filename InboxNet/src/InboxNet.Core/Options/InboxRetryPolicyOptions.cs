namespace InboxNet.Options;

public class InboxRetryPolicyOptions
{
    public int MaxRetries { get; set; } = 5;
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Multiplicative jitter band, clamped to <c>[0, 1]</c>. The actual delay is
    /// <c>cappedDelay × (1 + JitterFactor × U(-1, 1))</c>, so values up to 1.0 spread
    /// the delay between 0 and 2× of the base — preventing the additive form from
    /// going negative. Default: 0.2 (±20%).
    /// </summary>
    public double JitterFactor { get; set; } = 0.2;

    /// <summary>
    /// Returns <see cref="JitterFactor"/> clamped to <c>[0, 1]</c>.
    /// </summary>
    internal double ClampedJitterFactor => Math.Clamp(JitterFactor, 0.0, 1.0);
}

public sealed class InboxPurgeOptions
{
    /// <summary>
    /// How often the purge job runs. Default: 1 hour.
    /// </summary>
    public TimeSpan PurgeInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Retain processed and dead-lettered messages for at least this long before deletion.
    /// Default: 7 days.
    /// </summary>
    public TimeSpan RetainProcessed { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Retain handler-attempt rows for at least this long before deletion. Should generally
    /// be ≥ <see cref="RetainProcessed"/> so attempt forensics outlive their parent rows.
    /// Default: 30 days.
    /// </summary>
    public TimeSpan RetainAttempts { get; set; } = TimeSpan.FromDays(30);
}
