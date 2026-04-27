using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using InboxNet.AspNetCore;
using InboxNet.EntityFrameworkCore;
using InboxNet.EntityFrameworkCore.Extensions;
using InboxNet.Extensions;
using InboxNet.Processor.Extensions;
using InboxNet.Providers.Extensions;
using System.Reflection;

namespace InboxNet.LoadTests;

/// <summary>
/// Drives a single end-to-end run: builds the inbox host (receive endpoint + dispatcher +
/// SQL Server store), starts it on a local port, fires HMAC-signed webhooks at it, waits
/// for handler completion, and aggregates the results.
/// </summary>
public sealed class LoadTestRunner
{
    private readonly LoadTestConfig _config;

    public LoadTestRunner(LoadTestConfig config)
    {
        _config = config;
    }

    public async Task<LoadTestResult> RunAsync(CancellationToken ct)
    {
        var tracker = new LoadTestTracker { FailureRate = _config.HandlerFailureRate };

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(o => o.SingleLine = true);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.WebHost.UseUrls($"http://localhost:{_config.ReceiverPort}");

        builder.Services.AddSingleton(tracker);
        builder.Services.AddSingleton(_config);

        builder.Services
            .AddInboxNet(o =>
            {
                o.BatchSize = _config.BatchSize;
                o.MaxConcurrentDispatch = _config.MaxConcurrentDispatch;
                // Disable ordered processing: load test events have no entity affinity and we
                // want maximum parallelism through the dispatcher.
                o.EnableOrderedProcessing = false;
                o.BulkBookkeeping = _config.BulkBookkeeping;
                o.RecordAttemptsOnSuccess = _config.RecordAttemptsOnSuccess;
                o.RecordHandlerAttempts = _config.RecordHandlerAttempts;
            })
            .UseSqlServer(_config.ConnectionString,
            opt =>
            {
                // Simple name only — FullName includes Version/Culture/PublicKeyToken and
                // EF Core's migrations resolver won't match it against the loaded assembly.
                opt.MigrationsAssembly = Assembly.GetExecutingAssembly().GetName().Name;
            })
            .AddGenericHmacProvider(o =>
            {
                o.Key = "loadtest";
                o.Secret = _config.WebhookSecret;
                o.EventIdHeader = "X-Event-Id";
            })
            .AddHandler<LoadTestHandler>(r => r
                .ForProvider("loadtest")
                .WithMaxRetries(5))
            .AddBackgroundDispatcher(o =>
            {
                o.ColdPollingInterval = TimeSpan.FromMilliseconds(_config.ColdPollingIntervalMs);
            });

        await using var app = builder.Build();
        app.MapInboxWebhooks();

        // The library doesn't ship migrations, so the load test project owns them
        // (run `dotnet ef migrations add Init` from this project once). MigrateAsync
        // applies any pending migrations; clear leftover rows so prior runs don't pollute
        // metrics.
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InboxDbContext>();
            await db.Database.MigrateAsync(ct);

            if (_config.TruncateBeforeRun)
            {
                await db.InboxHandlerAttempts.ExecuteDeleteAsync(ct);
                await db.InboxMessages.ExecuteDeleteAsync(ct);
            }
        }

        await app.StartAsync(ct);

        var totalSw = Stopwatch.StartNew();

        // Publish phase.
        var client = new LoadTestClient(_config, tracker);
        var publishSw = Stopwatch.StartNew();
        var publishFailures = await client.PublishAllAsync(ct);
        publishSw.Stop();

        // Drain phase: wait until every published message has been handled (or timeout).
        var drainCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        drainCts.CancelAfter(_config.DrainTimeout);

        var expectedHandled = _config.TotalMessages - publishFailures;
        while (tracker.Handled < expectedHandled)
        {
            try
            {
                await Task.Delay(100, drainCts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        totalSw.Stop();

        await app.StopAsync(CancellationToken.None);

        var latencies = tracker.DrainLatenciesSorted();

        return new LoadTestResult
        {
            TotalPublished = _config.TotalMessages - publishFailures,
            PublishFailures = publishFailures,
            TotalHandled = tracker.Handled,
            HandlerFailuresInjected = tracker.FailuresInjected,
            DuplicatesDetected = tracker.DuplicatesDetected,
            UnexpectedHandled = tracker.UnexpectedHandled,
            Lost = tracker.LostCount(),
            PublishDuration = publishSw.Elapsed,
            TotalDuration = totalSw.Elapsed,
            LatenciesSortedMs = latencies
        };
    }
}
