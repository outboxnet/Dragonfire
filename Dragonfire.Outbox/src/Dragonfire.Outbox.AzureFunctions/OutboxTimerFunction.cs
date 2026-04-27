using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Dragonfire.Outbox.Interfaces;

namespace Dragonfire.Outbox.AzureFunctions;

public sealed class OutboxTimerFunction
{
    private readonly IOutboxProcessor _processor;
    private readonly ILogger<OutboxTimerFunction> _logger;

    public OutboxTimerFunction(
        IOutboxProcessor processor,
        ILogger<OutboxTimerFunction> logger)
    {
        _processor = processor;
        _logger = logger;
    }

    /// <summary>
    /// Configures the timer schedule via the <c>Outbox:TimerCron</c> app setting.
    /// Required app setting example (every 10 seconds):
    ///   "Outbox:TimerCron": "*/10 * * * * *"
    /// Add this to local.settings.json under "Values" or to Azure App Settings.
    /// </summary>
    [Function("OutboxProcessor")]
    public async Task RunAsync(
        [TimerTrigger("%Outbox:TimerCron%")] TimerInfo timer,
        CancellationToken ct)
    {
        _logger.LogDebug("Outbox timer function triggered at {Time}", DateTimeOffset.UtcNow);
        _ = await _processor.ProcessBatchAsync(ct);
    }
}
