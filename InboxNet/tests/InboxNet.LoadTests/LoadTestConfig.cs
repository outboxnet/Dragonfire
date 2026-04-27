namespace InboxNet.LoadTests;

/// <summary>
/// Knobs for a single inbox load test run. Drives both the publisher (HTTP webhook client)
/// and the receiver/dispatcher pipeline being exercised.
/// </summary>
public sealed class LoadTestConfig
{
    /// <summary>SQL Server connection string used by the inbox persistence store.</summary>
    public string ConnectionString { get; set; } =
        "Server=(localdb)\\MSSQLLocalDB;Database=InboxNetLoadTest;Integrated Security=true;TrustServerCertificate=true;";

    /// <summary>Total number of webhook POSTs the publisher will issue.</summary>
    public int TotalMessages { get; set; } = 5000;

    /// <summary>Maximum number of webhook POSTs in flight concurrently.</summary>
    public int PublisherConcurrency { get; set; } = 20;

    /// <summary>Inbox dispatcher batch size (cold-path scan).</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>Maximum number of messages dispatched concurrently within a batch.</summary>
    public int MaxConcurrentDispatch { get; set; } = 10;

    /// <summary>Cold-path polling interval (milliseconds).</summary>
    public int ColdPollingIntervalMs { get; set; } = 1000;

    /// <summary>TCP port the receiving ASP.NET Core host listens on.</summary>
    public int ReceiverPort { get; set; } = 5556;

    /// <summary>HMAC-SHA256 shared secret used by both the publisher and the provider.</summary>
    public string WebhookSecret { get; set; } = "loadtest-secret";

    /// <summary>
    /// Probability (0..1) that a handler invocation throws on first attempt. Used to exercise
    /// the retry path. Throws are deterministic per <c>(eventId, attempt)</c> so a message
    /// eventually succeeds.
    /// </summary>
    public double HandlerFailureRate { get; set; } = 0.5;

    /// <summary>How long to wait for all handler completions after publishing finishes.</summary>
    public TimeSpan DrainTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Truncate the inbox tables before the run so metrics aren't polluted.</summary>
    public bool TruncateBeforeRun { get; set; } = true;

    /// <summary>
    /// Mirrors <c>InboxOptions.BulkBookkeeping</c>. When true (default) the dispatcher
    /// flushes mark-as-processed UPDATEs and attempt INSERTs once per batch.
    /// </summary>
    public bool BulkBookkeeping { get; set; } = true;

    /// <summary>
    /// Mirrors <c>InboxOptions.RecordAttemptsOnSuccess</c>. When false, attempt rows are
    /// only written for failures.
    /// </summary>
    public bool RecordAttemptsOnSuccess { get; set; } = false;

    /// <summary>
    /// Mirrors <c>InboxOptions.RecordHandlerAttempts</c>. When false, the attempt store is
    /// bypassed entirely; handlers must be idempotent on <c>InboxMessage.Id</c>.
    /// </summary>
    public bool RecordHandlerAttempts { get; set; } = false;
}
