using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace OutboxNet.LoadTests;

/// <summary>
/// Embedded Kestrel HTTP server that acts as the webhook target during load tests.
///
/// For each POST to /webhook it:
///   1. Reads the body and verifies the HMAC-SHA256 signature (optional).
///   2. Injects a 503 with probability <see cref="FailureRate"/> to exercise the retry pipeline.
///   3. Records the X-Outbox-Correlation-Id for correctness verification and latency calculation.
///
/// Thread-safe — designed for concurrent delivery by the outbox processor.
/// </summary>
public sealed class WebhookReceiver
{
    // correlationId → time of FIRST successful receipt (for latency)
    private readonly ConcurrentDictionary<string, DateTimeOffset> _firstReceived = new();
    // correlationId → total number of delivery attempts received (including retries)
    private readonly ConcurrentDictionary<string, int> _deliveryCounts = new();

    private readonly string _secret;
    private readonly bool _verifyHmac;
    private long _failureInjected;   // count of 503s returned (for diagnostics)
    private long _hmacRejected;      // count of 401s returned (HMAC mismatch)

    // Using a per-instance Random is fine: failure rate doesn't need cryptographic quality.
    private readonly Random _rng = new();

    private WebApplication? _app;

    public double FailureRate { get; }

    // ── Metrics ───────────────────────────────────────────────────────────────

    /// <summary>Number of unique correlation IDs received (successful deliveries).</summary>
    public int UniqueDelivered => _firstReceived.Count;

    /// <summary>Total POST requests handled (including retries and injected failures).</summary>
    public long TotalRequests => _deliveryCounts.Values.Sum();

    /// <summary>Correlation IDs delivered more than once (duplicates sent by the processor).</summary>
    public int DuplicateDeliveries => _deliveryCounts.Count(kvp => kvp.Value > 1);

    /// <summary>503 responses injected to test the retry pipeline.</summary>
    public long FailuresInjected => Interlocked.Read(ref _failureInjected);

    /// <summary>401 responses due to HMAC mismatch (correctness signal).</summary>
    public long HmacRejections => Interlocked.Read(ref _hmacRejected);

    /// <summary>Point-in-time snapshot: correlationId → first delivery timestamp.</summary>
    public IReadOnlyDictionary<string, DateTimeOffset> FirstReceivedAt => _firstReceived;

    /// <summary>
    /// Clears all delivery tracking. Called after the warmup drain so warmup
    /// messages delivered late don't show up as "unexpected" in the load phase.
    /// </summary>
    public void Reset()
    {
        _firstReceived.Clear();
        _deliveryCounts.Clear();
        Interlocked.Exchange(ref _failureInjected, 0);
        Interlocked.Exchange(ref _hmacRejected, 0);
    }

    // ─────────────────────────────────────────────────────────────────────────

    public WebhookReceiver(string secret, double failureRate, bool verifyHmac)
    {
        _secret      = secret;
        FailureRate  = failureRate;
        _verifyHmac  = verifyHmac;
    }

    public async Task StartAsync(int port, CancellationToken ct = default)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            // Suppress the startup banner and environment-variable scanning.
            EnvironmentName = "LoadTest"
        });

        builder.WebHost.UseUrls($"http://localhost:{port}");

        // Suppress ASP.NET Core request logs — they would flood the console at load.
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(opts => opts.LogToStandardErrorThreshold = LogLevel.Error);

        _app = builder.Build();

        _app.MapPost("/webhook", async (HttpRequest request) =>
        {
            // Always read the body: needed for HMAC even if we later inject a failure.
            string rawBody;
            using (var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: false))
                rawBody = await reader.ReadToEndAsync();

            // ── HMAC verification ─────────────────────────────────────────────
            if (_verifyHmac)
            {
                var expectedHex = "sha256=" + Convert.ToHexString(
                    HMACSHA256.HashData(
                        Encoding.UTF8.GetBytes(_secret),
                        Encoding.UTF8.GetBytes(rawBody)));

                var receivedSig = request.Headers["X-Outbox-Signature"].ToString();

                // Normalise to lower-case before fixed-time comparison (hex is case-insensitive).
                var expectedBytes = Encoding.ASCII.GetBytes(expectedHex.ToLowerInvariant());
                var receivedBytes = Encoding.ASCII.GetBytes(receivedSig.ToLowerInvariant());

                if (expectedBytes.Length != receivedBytes.Length
                    || !CryptographicOperations.FixedTimeEquals(expectedBytes, receivedBytes))
                {
                    Interlocked.Increment(ref _hmacRejected);
                    return Results.Unauthorized();
                }
            }

            // ── Failure injection (after HMAC so we know the request was genuine) ──
            if (FailureRate > 0 && _rng.NextDouble() < FailureRate)
            {
                Interlocked.Increment(ref _failureInjected);
                return Results.StatusCode(503);
            }

            // ── Record delivery ───────────────────────────────────────────────
            var correlationId = request.Headers["X-Outbox-Correlation-Id"].ToString();
            if (!string.IsNullOrEmpty(correlationId))
            {
                var now = DateTimeOffset.UtcNow;
                _firstReceived.TryAdd(correlationId, now);
                _deliveryCounts.AddOrUpdate(correlationId, 1, (_, c) => c + 1);
            }

            return Results.Ok();
        });

        // Health probe used by the runner to confirm the receiver is ready.
        _app.MapGet("/health", () => Results.Ok("ready"));

        await _app.StartAsync(ct);
    }

    public async Task StopAsync()
    {
        if (_app is not null)
            await _app.StopAsync();
    }
}
