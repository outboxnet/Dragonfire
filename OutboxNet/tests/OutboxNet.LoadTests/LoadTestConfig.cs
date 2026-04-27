namespace OutboxNet.LoadTests;

/// <summary>
/// All configurable parameters for a load test run.
/// </summary>
public sealed class LoadTestConfig
{
    // ── Database ──────────────────────────────────────────────────────────────
    public string ConnectionString { get; init; } =
        "Server=(localdb)\\mssqllocaldb;Database=OutboxLoadTest;Trusted_Connection=True;" +
        "MultipleActiveResultSets=True;TrustServerCertificate=True;";

    // ── Publishing ────────────────────────────────────────────────────────────
    /// <summary>Total outbox messages to publish during the load phase.</summary>
    public int TotalMessages { get; init; } = 5000;

    /// <summary>Max concurrent publish goroutines (each opens its own DB transaction).</summary>
    public int PublisherConcurrency { get; init; } = 20;

    // ── Processing ────────────────────────────────────────────────────────────
    /// <summary>OutboxOptions.BatchSize — rows per LockNextBatch SQL call.</summary>
    public int BatchSize { get; init; } = 50;

    /// <summary>OutboxOptions.MaxConcurrentDeliveries — messages processed in parallel.</summary>
    public int MaxConcurrentDeliveries { get; init; } = 10;

    /// <summary>Cold-path polling interval for the background processor.</summary>
    public TimeSpan ColdPollingInterval { get; init; } = TimeSpan.FromMilliseconds(3000);

    // ── Receiver ──────────────────────────────────────────────────────────────
    /// <summary>Local TCP port for the embedded webhook receiver.</summary>
    public int ReceiverPort { get; init; } = 5556;

    /// <summary>HMAC-SHA256 signing secret shared between publisher and receiver.</summary>
    public string WebhookSecret { get; init; } = "load-test-secret-key-change-me";

    /// <summary>
    /// Fraction of webhook POST requests the receiver returns 503 for (0–1).
    /// Exercises the retry scheduler. Even with 20% failure rate, messages eventually
    /// succeed because retries keep firing until MaxRetries is exhausted.
    /// </summary>
    public double WebhookFailureRate { get; init; } = 0.0;

    /// <summary>Verify the HMAC-SHA256 signature on every received webhook.</summary>
    public bool VerifyHmac { get; init; } = true;

    // ── Drain ─────────────────────────────────────────────────────────────────
    /// <summary>
    /// How long to wait after publishing stops for all messages to be delivered.
    /// Lost = still undelivered when this expires.
    /// </summary>
    public TimeSpan DrainTimeout { get; init; } = TimeSpan.FromSeconds(120);

    // ── DB lifecycle ──────────────────────────────────────────────────────────
    /// <summary>
    /// Truncate OutboxMessages and DeliveryAttempts before the run so leftover data
    /// from a previous run doesn't skew results. Set false with --keep-data to skip.
    /// </summary>
    public bool TruncateBeforeRun { get; init; } = true;

    // ─────────────────────────────────────────────────────────────────────────

    public static LoadTestConfig Parse(string[] args)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--")) continue;
            var key = args[i][2..];
            // Flag (no following value): --keep-data
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--"))
                d[key] = "true";
            else
                d[key] = args[++i];
        }

        return new LoadTestConfig
        {
            ConnectionString = d.GetValueOrDefault("connection",
                "Server=(localdb)\\mssqllocaldb;Database=OutboxLoadTest;Trusted_Connection=True;" +
                "MultipleActiveResultSets=True;TrustServerCertificate=True;"),
            TotalMessages          = int.Parse(d.GetValueOrDefault("messages",    "5000")),
            PublisherConcurrency   = int.Parse(d.GetValueOrDefault("concurrency", "20")),
            BatchSize              = int.Parse(d.GetValueOrDefault("batch-size",  "50")),
            MaxConcurrentDeliveries= int.Parse(d.GetValueOrDefault("max-deliveries","10")),
            ColdPollingInterval    = TimeSpan.FromMilliseconds(int.Parse(d.GetValueOrDefault("poll-ms","3000"))),
            ReceiverPort           = int.Parse(d.GetValueOrDefault("port",        "5556")),
            WebhookSecret          = d.GetValueOrDefault("secret",  "load-test-secret-key-change-me"),
            WebhookFailureRate     = double.Parse(d.GetValueOrDefault("failure-rate", "0.0"),
                                        System.Globalization.CultureInfo.InvariantCulture),
            VerifyHmac             = !d.ContainsKey("no-hmac"),
            DrainTimeout           = TimeSpan.FromSeconds(int.Parse(d.GetValueOrDefault("drain-timeout","120"))),
            TruncateBeforeRun      = !d.ContainsKey("keep-data"),
        };
    }

    public void Print()
    {
        Console.WriteLine("  Database:          " + ConnectionString);
        Console.WriteLine($"  Messages:          {TotalMessages:N0}");
        Console.WriteLine($"  Publisher threads: {PublisherConcurrency}");
        Console.WriteLine($"  Batch size:        {BatchSize}");
        Console.WriteLine($"  Max deliveries:    {MaxConcurrentDeliveries} concurrent");
        Console.WriteLine($"  Cold poll:         {ColdPollingInterval.TotalMilliseconds:F0} ms");
        Console.WriteLine($"  Receiver port:     {ReceiverPort}");
        Console.WriteLine($"  Failure rate:      {WebhookFailureRate:P0}");
        Console.WriteLine($"  Verify HMAC:       {VerifyHmac}");
        Console.WriteLine($"  Drain timeout:     {DrainTimeout.TotalSeconds:F0}s");
    }
}
