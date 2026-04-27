using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using SyncLib.Core;

namespace SyncLib.Sample.AzureFunctions;

/// <summary>
/// One CRON-driven function that runs every registered stream's sync. The
/// runner applies the same retry/circuit-breaker/state/metrics pipeline used
/// by <see cref="SyncOrchestrator"/>; only the scheduling source is different.
/// </summary>
public sealed class SyncTimerFunction
{
    private readonly ISyncRunner _runner;
    private readonly ILogger<SyncTimerFunction> _logger;

    public SyncTimerFunction(ISyncRunner runner, ILogger<SyncTimerFunction> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    /// <summary>Run all streams every 5 minutes (configure in host.json or via app setting).</summary>
    [Function("RunAllSyncs")]
    public async Task RunAll(
        [TimerTrigger("0 */5 * * * *")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Timer fired at {Now:o}; streams: {Streams}",
            DateTime.UtcNow, string.Join(",", _runner.RegisteredStreams.Select(k => k.ToString())));

        var summary = await _runner.RunAllAsync(cancellationToken);

        _logger.LogInformation("Sync summary: {Total} total, {Ok} succeeded, {Failed} failed, {Skipped} skipped.",
            summary.TotalStreams, summary.Succeeded, summary.Failed, summary.Skipped);

        if (!summary.IsHealthy)
        {
            // Surface a non-success outcome so AppInsights / portal can alert.
            // In Functions Worker, throwing causes the invocation to be marked failed.
            throw new InvalidOperationException(
                "One or more streams failed: " +
                string.Join("; ", summary.StreamErrors.Select(kv => $"{kv.Key}: {kv.Value}")));
        }
    }
}
