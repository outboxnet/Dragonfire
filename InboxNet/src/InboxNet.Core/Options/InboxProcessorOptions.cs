namespace InboxNet.Options;

public class InboxProcessorOptions
{
    /// <summary>
    /// Cold-path scan interval for messages that arrived on a different instance or are
    /// due for retry. The hot-path channel delivers same-instance messages immediately.
    /// On idle (empty batch) the actual interval grows up to <see cref="ColdMaxPollingInterval"/>.
    /// Default: 1 second.
    /// </summary>
    public TimeSpan ColdPollingInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Upper bound for adaptive cold polling. After consecutive empty scans the interval
    /// doubles up to this value to avoid burning DB cycles on idle deployments. The first
    /// non-empty scan resets the interval to <see cref="ColdPollingInterval"/>. Set equal
    /// to <see cref="ColdPollingInterval"/> to disable adaptive backoff.
    /// Default: 30 seconds.
    /// </summary>
    public TimeSpan ColdMaxPollingInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Throttle for <c>ReleaseExpiredLocksAsync</c> (a table-wide UPDATE). Each cold-path
    /// scan checks the elapsed time and only triggers the lock-release sweep when this
    /// interval has passed. Default: 30 seconds.
    /// </summary>
    public TimeSpan ReleaseExpiredLocksInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum number of message IDs the hot path drains from the channel before flushing.
    /// Larger batches amortise bookkeeping round-trips at the cost of dispatch latency.
    /// Default: 32.
    /// </summary>
    public int HotPathBatchSize { get; set; } = 32;

    /// <summary>
    /// Maximum time the hot path waits while accumulating IDs before flushing a partial
    /// batch. Bounds worst-case dispatch latency under low arrival rates.
    /// Default: 5 ms.
    /// </summary>
    public TimeSpan HotPathBatchWindow { get; set; } = TimeSpan.FromMilliseconds(5);
}
