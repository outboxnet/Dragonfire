using System.Collections.Concurrent;
using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Dragonfire.Outbox.EntityFrameworkCore;
using Dragonfire.Outbox.EntityFrameworkCore.Extensions;
using Dragonfire.Outbox.Extensions;
using Dragonfire.Outbox.Interfaces;
using Dragonfire.Outbox.Options;
using Dragonfire.Outbox.Processor.Extensions;
using Dragonfire.Outbox.Delivery.Extensions;

namespace Dragonfire.Outbox.LoadTests;

/// <summary>
/// Orchestrates the full load test lifecycle using DECOUPLED PHASES so that
/// the publisher is never competing with the processor for CPU / threadpool —
/// this is critical for accurate latency measurement on modest hardware.
///
///   1. Setup    — create DB schema, start embedded webhook receiver
///   2. Warmup   — publish 100 msgs through a full host to JIT-compile hot
///                 paths and fill connection pools
///   3. Publish  — publish N msgs with the processor NOT running
///                 (rows pile up in DB, no delivery yet)
///   4. Dispatch — start a fresh processor host; this is the measurement t0
///   5. Drain    — wait for the processor to clear the backlog
///   6. Verify   — correctness check (lost, duplicates, HMAC)
///   7. Report   — compute and return <see cref="LoadTestResult"/>
///
/// The latency metric is then "time-in-system since the message became
/// deliverable", i.e. <c>receivedAt − max(commitAt, dispatchStart)</c>.
/// Under decoupled mode every commit precedes dispatchStart, so the metric
/// reduces to <c>receivedAt − dispatchStart</c> — a pure measure of how
/// quickly the processor works through a backlog with no publisher noise.
/// </summary>
public sealed class LoadTestRunner
{
    private readonly LoadTestConfig _config;

    // correlationId → timestamp immediately after tx.CommitAsync() returns
    private readonly ConcurrentDictionary<string, DateTimeOffset> _published = new();

    private DateTimeOffset _publishStart;
    private DateTimeOffset _publishEnd;
    // Instant at which the measurement processor host starts.
    // Used as the lower bound for per-message latency (see ComputeResult).
    private DateTimeOffset _dispatchStart;

    public LoadTestRunner(LoadTestConfig config) => _config = config;

    // ─────────────────────────────────────────────────────────────────────────

    public async Task<LoadTestResult> RunAsync(CancellationToken ct = default)
    {
        // ── PHASE 1: Start receiver (stays up for the whole run) ──────────────
        Console.WriteLine("[1/6] Starting webhook receiver...");
        var receiver = new WebhookReceiver(_config.WebhookSecret, _config.WebhookFailureRate, _config.VerifyHmac);
        await receiver.StartAsync(_config.ReceiverPort, ct);
        await WaitForReceiverAsync(_config.ReceiverPort, ct);
        Console.WriteLine($"      http://localhost:{_config.ReceiverPort}/webhook  (failure rate: {_config.WebhookFailureRate:P0})");

        // ── PHASE 2: Build a SINGLE host with everything wired up ─────────────
        // Publisher, processor, delivery, and subscription all live in the same
        // DI container → the same IOutboxSignal singleton. This is required for
        // hot-path notifications to work: the publisher's Notify(messageId) call
        // pushes to the exact channel the processor is reading from, so messages
        // can be delivered in <1 ms without waiting for the cold-path poll.
        Console.WriteLine("[2/6] Building coupled host (publisher + processor + delivery)...");
        using var host = BuildOutboxHost(withProcessor: true);

        Console.WriteLine("[3/6] Creating schema...");
        await SetupDatabaseAsync(host, ct);

        Console.WriteLine("      Starting host...");
        await host.StartAsync(ct);

        try
        {
            // ── PHASE 3: Warmup ───────────────────────────────────────────────
            // Primes JIT, EF Core model cache, HttpClient handlers, thread-pool.
            // Messages are published with warmup:true so their IDs never enter
            // _published and don't pollute the final correctness check.
            Console.WriteLine("[4/6] Warming up (100 messages)...");
            const int warmupCount = 100;
            await PublishBatchAsync(host, warmupCount, warmup: true, ct);
            // Pass warmupCount explicitly — _published.Count is 0 during warmup.
            await DrainAsync(receiver, warmupCount, TimeSpan.FromSeconds(30), label: "warmup", ct);
            var warmupDelivered = receiver.UniqueDelivered;
            _published.Clear();
            receiver.Reset();   // drop warmup state so it can't appear as "unexpected"
            Console.WriteLine($"      Warmup done — {warmupDelivered} delivered, JIT primed.");

            // ── PHASE 4: Load — publish + process CONCURRENTLY ───────────────
            // The processor is running the whole time; every publish triggers
            // a hot-path Notify() which wakes the Channel consumer immediately.
            // Messages that the hot path can't get to (because the Channel is
            // saturated, or the processor is already busy at MaxConcurrentDeliveries)
            // will be picked up by the next cold-path poll instead.
            Console.WriteLine($"[5/6] Publishing {_config.TotalMessages:N0} messages" +
                              $" ({_config.PublisherConcurrency} concurrent threads, processor running)...");
            // Set _dispatchStart BEFORE publishing. Every message's commit time
            // is > _dispatchStart, so the ComputeResult formula
            //     readyAt = max(commit, _dispatchStart)
            // reduces to commit time — giving true end-to-end commit→deliver latency.
            _dispatchStart = DateTimeOffset.UtcNow;
            _publishStart  = DateTimeOffset.UtcNow;
            await PublishBatchAsync(host, _config.TotalMessages, warmup: false, ct);
            _publishEnd    = DateTimeOffset.UtcNow;

            var publishRate = _published.Count / Math.Max((_publishEnd - _publishStart).TotalSeconds, 0.001);
            Console.WriteLine($"      Published {_published.Count:N0} messages in " +
                              $"{(_publishEnd - _publishStart).TotalSeconds:F1}s  ({publishRate:F0} msg/s)");

            // ── PHASE 5: Drain ────────────────────────────────────────────────
            Console.WriteLine($"[6/6] Draining (timeout: {_config.DrainTimeout.TotalSeconds:F0}s)...");
            var drainEnd = await DrainAsync(receiver, _published.Count, _config.DrainTimeout, label: "load", ct);
            Console.WriteLine();

            // ── PHASE 6: Compute result ──────────────────────────────────────
            return ComputeResult(receiver, drainEnd);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
            await receiver.StopAsync();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Publishing
    // ─────────────────────────────────────────────────────────────────────────

    private async Task PublishBatchAsync(IHost host, int count, bool warmup, CancellationToken ct)
    {
        await Parallel.ForEachAsync(
            Enumerable.Range(0, count),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _config.PublisherConcurrency,
                CancellationToken      = ct
            },
            async (i, token) =>
            {
                var correlationId = Guid.NewGuid().ToString("N");

                // Each publish needs its own DI scope: EfCoreOutboxPublisher and
                // OutboxDbContext are registered as Scoped.
                await using var scope     = host.Services.CreateAsyncScope();
                var db                    = scope.ServiceProvider.GetRequiredService<LoadTestDbContext>();
                var publisher             = scope.ServiceProvider.GetRequiredService<IOutboxPublisher>();

                // Begin a transaction on LoadTestDbContext so the publisher can
                // enlist OutboxDbContext in the same connection/transaction —
                // making the outbox INSERT atomic with the domain write.
                await using var tx = await db.Database.BeginTransactionAsync(token);

                await publisher.PublishAsync(
                    eventType:      "load.test.event",
                    payload:        new { Seq = i, Cid = correlationId },
                    correlationId:  correlationId,
                    cancellationToken: token);

                await tx.CommitAsync(token);

                // Record after commit: this is the "published at" timestamp
                // that defines the start of end-to-end latency measurement.
                if (!warmup)
                    _published.TryAdd(correlationId, DateTimeOffset.UtcNow);
            });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Drain
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Polls until <paramref name="expectedCount"/> unique deliveries are recorded
    /// or <paramref name="timeout"/> expires. Prints a live progress line.
    /// Returns the timestamp when polling stopped.
    /// </summary>
    private static async Task<DateTimeOffset> DrainAsync(
        WebhookReceiver receiver,
        int expectedCount,
        TimeSpan timeout,
        string label,
        CancellationToken ct)
    {
        var deadline        = DateTimeOffset.UtcNow.Add(timeout);
        var lastCount       = -1;
        var lastProgressAt  = DateTimeOffset.UtcNow;

        // Tolerate slow cold-path polls: only give up if we've been idle
        // (no new deliveries) for this long *and* the wall-clock timeout expired.
        // This means a ColdPollingInterval of 3s won't cause a premature "lost" verdict.
        var idleTolerance   = TimeSpan.FromSeconds(15);

        while (receiver.UniqueDelivered < expectedCount)
        {
            var current = receiver.UniqueDelivered;
            if (current != lastCount)
            {
                lastCount      = current;
                lastProgressAt = DateTimeOffset.UtcNow;
                var pct        = expectedCount > 0 ? (double)current / expectedCount * 100 : 100;
                var remain     = (deadline - DateTimeOffset.UtcNow).TotalSeconds;
                Console.Write($"\r      [{label}] {current:N0}/{expectedCount:N0}  ({pct:F1}%)  timeout in {Math.Max(remain, 0):F0}s   ");
            }

            var now      = DateTimeOffset.UtcNow;
            var timedOut = now >= deadline;
            var idleTooLong = (now - lastProgressAt) >= idleTolerance;

            // Exit only when BOTH the wall-clock deadline has passed AND we've
            // seen no progress for idleTolerance. This gives slow pollers a
            // chance to finish what they've started.
            if (timedOut && idleTooLong) break;

            await Task.Delay(200, ct);
        }

        return DateTimeOffset.UtcNow;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Infrastructure
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds an Dragonfire.Outbox host. When <paramref name="withProcessor"/> is false
    /// the background processor, delivery, and subscription are omitted — the
    /// host only provides the publisher + DB wiring. Used by the decoupled
    /// publish phase to avoid any CPU contention between publishing and
    /// delivery, which on slow hardware dominates the observed latency.
    /// </summary>
    private IHost BuildOutboxHost(bool withProcessor)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                // Suppress chatty debug output during the load; operators can
                // pipe to a file and inspect later. Warnings/errors still show.
                logging.SetMinimumLevel(LogLevel.Warning);
            })
            .ConfigureServices((_, services) =>
            {
                // User DbContext — no entities, used only for transaction management.
                services.AddDbContext<LoadTestDbContext>(opts =>
                    opts.UseSqlServer(_config.ConnectionString));

                var builder = services
                    .AddOutboxNet(opts =>
                    {
                        opts.BatchSize                    = _config.BatchSize;
                        opts.MaxConcurrentDeliveries      = _config.MaxConcurrentDeliveries;
                        opts.DefaultVisibilityTimeout     = TimeSpan.FromMinutes(2);
                        // Ordered processing adds a NOT EXISTS subquery per row — disable
                        // for the load test so we measure raw throughput without the overhead.
                        opts.EnableOrderedProcessing      = false;
                    })
                    // EF Core stores + EfCoreOutboxPublisher<LoadTestDbContext>
                    .UseSqlServerContext<LoadTestDbContext>(_config.ConnectionString);

                if (withProcessor)
                {
                    builder
                        // Hot-path Channel + cold-path poll loop
                        .AddBackgroundProcessor(opts =>
                        {
                            opts.ColdPollingInterval = _config.ColdPollingInterval;
                        })
                        // HTTP delivery with HMAC-SHA256 signing
                        .AddWebhookDelivery(opts =>
                        {
                            opts.HttpTimeout         = TimeSpan.FromSeconds(10);
                            opts.Retry.MaxRetries    = 3;
                            opts.Retry.BaseDelay     = TimeSpan.FromSeconds(1);
                            opts.Retry.MaxDelay      = TimeSpan.FromSeconds(30);
                            opts.Retry.JitterFactor  = 0.1;
                        })
                        // Config-driven subscription: all messages → local receiver
                        .UseConfigWebhooks(opts =>
                        {
                            opts.Mode   = WebhookMode.Global;
                            opts.Global = new WebhookEndpointConfig
                            {
                                Url            = $"http://localhost:{_config.ReceiverPort}/webhook",
                                Secret         = _config.WebhookSecret,
                                MaxRetries     = 3,
                                TimeoutSeconds = 10,
                            };
                        });
                }
                // If !withProcessor: IOutboxSignal (registered by AddOutboxNet)
                // still accepts Notify() calls from the publisher, they just
                // go into a channel nobody reads. That's fine — the next phase
                // will rely on the cold-path DB poll, not the channel.
            })
            .Build();
    }

    private async Task SetupDatabaseAsync(IHost host, CancellationToken ct)
    {
        await using var scope   = host.Services.CreateAsyncScope();
        var outboxDb            = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();

        // Creates the database + outbox schema ([outbox].[OutboxMessages], etc.)
        // from the EF Core model. No-op if the schema already exists.
        await outboxDb.Database.EnsureCreatedAsync(ct);
        Console.WriteLine("      Schema ready.");

        if (_config.TruncateBeforeRun)
        {
            // Remove any leftover data from a previous run so latencies aren't
            // skewed by rows the processor hasn't caught up on.
            // Delete DeliveryAttempts first due to FK → OutboxMessages.
            var deletedAttempts  = await outboxDb.DeliveryAttempts.ExecuteDeleteAsync(ct);
            var deletedMessages  = await outboxDb.OutboxMessages.ExecuteDeleteAsync(ct);
            Console.WriteLine($"      Cleared {deletedMessages:N0} messages and {deletedAttempts:N0} attempts from previous runs.");
        }
    }

    /// <summary>
    /// Sends a GET /health to the receiver and retries until it responds, so we
    /// don't start publishing before the server is ready.
    /// </summary>
    private static async Task WaitForReceiverAsync(int port, CancellationToken ct)
    {
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                var resp = await http.GetAsync("/health", ct);
                if (resp.IsSuccessStatusCode) return;
            }
            catch { /* not ready yet */ }
            await Task.Delay(100, ct);
        }
        throw new InvalidOperationException($"Webhook receiver on port {port} did not become ready.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Result computation
    // ─────────────────────────────────────────────────────────────────────────

    private LoadTestResult ComputeResult(WebhookReceiver receiver, DateTimeOffset drainEnd)
    {
        var publishedIds  = new HashSet<string>(_published.Keys);
        var receivedIds   = new HashSet<string>(receiver.FirstReceivedAt.Keys);

        var lostCount       = publishedIds.Except(receivedIds).Count();
        var unexpectedCount = receivedIds.Except(publishedIds).Count();

        // Per-message latency = receivedAt − max(commitAt, dispatchStart).
        //
        // In decoupled mode all commitAts precede dispatchStart, so this
        // measures "time from processor cold-start to delivery", which is
        // what we actually want on a slow laptop — the publisher's jitter,
        // GC pauses, and threadpool saturation no longer pollute the number.
        //
        // (If a future variant publishes while the processor is running, the
        // max(...) correctly falls back to commit-time for messages born after
        // dispatchStart, so the metric still means "time since deliverable".)
        var latencies = _published
            .Where(kvp => receiver.FirstReceivedAt.TryGetValue(kvp.Key, out _))
            .Select(kvp =>
            {
                var readyAt = kvp.Value > _dispatchStart ? kvp.Value : _dispatchStart;
                return (receiver.FirstReceivedAt[kvp.Key] - readyAt).TotalMilliseconds;
            })
            .Where(ms => ms >= 0)   // guard: clock skew / same-millisecond deliver
            .OrderBy(ms => ms)
            .ToArray();

        var publishDuration  = (_publishEnd - _publishStart).TotalSeconds;
        var totalDuration    = (drainEnd    - _publishStart).TotalSeconds;

        // Retry deliveries = total POST calls to receiver − unique successful deliveries.
        var retryDeliveries  = (int)(receiver.TotalRequests - receiver.UniqueDelivered);

        return new LoadTestResult
        {
            Config              = _config,
            TotalPublished      = _published.Count,
            TotalDelivered      = receiver.UniqueDelivered,
            TotalRetryDeliveries= Math.Max(retryDeliveries, 0),
            PublishThroughput   = _published.Count / Math.Max(publishDuration, 0.001),
            DeliveryThroughput  = receiver.UniqueDelivered / Math.Max(totalDuration, 0.001),
            PublishDurationSec  = publishDuration,
            TotalDurationSec    = totalDuration,
            LostCount           = lostCount,
            DuplicateCount      = receiver.DuplicateDeliveries,
            UnexpectedCount     = unexpectedCount,
            HmacRejections      = receiver.HmacRejections,
            FailuresInjected    = receiver.FailuresInjected,
            LatenciesSorted     = latencies,
        };
    }
}
