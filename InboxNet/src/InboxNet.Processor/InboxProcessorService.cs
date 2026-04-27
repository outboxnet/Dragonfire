using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using InboxNet.Interfaces;
using InboxNet.Options;

namespace InboxNet.Processor;

/// <summary>
/// Inbox dispatcher hosted service. Runs a hot path (drains the in-process signal channel,
/// coalesces arrivals into a small batch, and dispatches them in one round-trip for
/// sub-millisecond same-instance latency) and a cold path (adaptive batch scan for
/// cross-instance messages, retries, and overflow recovery). The DB lock gate guarantees
/// at-most-once dispatch across replicas.
/// </summary>
public sealed class InboxProcessorService : BackgroundService
{
    private readonly IInboxProcessor _processor;
    private readonly IInboxSignal _signal;
    private readonly InboxProcessorOptions _processorOptions;
    private readonly InboxOptions _inboxOptions;
    private readonly ILogger<InboxProcessorService> _logger;

    // IDs currently being dispatched by the hot path. The cold path skips these so fresh
    // same-instance messages are handled exclusively by the hot path, and the cold path
    // focuses on cross-instance messages, retries, and channel-overflow recovery.
    private readonly ConcurrentDictionary<Guid, byte> _hotInFlight = new();

    public InboxProcessorService(
        IInboxProcessor processor,
        IInboxSignal signal,
        IOptions<InboxProcessorOptions> processorOptions,
        IOptions<InboxOptions> inboxOptions,
        ILogger<InboxProcessorService> logger)
    {
        _processor = processor;
        _signal = signal;
        _processorOptions = processorOptions.Value;
        _inboxOptions = inboxOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Inbox dispatcher started. Cold polling: {Min}ms..{Max}ms, hot batch: {BatchSize}/{WindowMs}ms, MaxConcurrentDispatch: {Max}",
            _processorOptions.ColdPollingInterval.TotalMilliseconds,
            _processorOptions.ColdMaxPollingInterval.TotalMilliseconds,
            _processorOptions.HotPathBatchSize,
            _processorOptions.HotPathBatchWindow.TotalMilliseconds,
            _inboxOptions.MaxConcurrentDispatch);

        // Both loops are essential. If either one exits — normally on shutdown or due to a
        // terminal failure — cancel the survivor so the host sees a single deterministic stop
        // rather than the service silently degrading to one path.
        using var loopCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var hot = RunHotPathAsync(loopCts.Token);
        var cold = RunColdPathAsync(loopCts.Token);

        var first = await Task.WhenAny(hot, cold);
        if (first.IsFaulted && !stoppingToken.IsCancellationRequested)
        {
            _logger.LogError(first.Exception,
                "Inbox dispatcher loop terminated unexpectedly — stopping the survivor");
        }
        loopCts.Cancel();

        try
        {
            await Task.WhenAll(hot, cold);
        }
        catch (OperationCanceledException)
        {
            // Expected once we cancel the survivor.
        }
    }

    private async Task RunHotPathAsync(CancellationToken ct)
    {
        var batchSize = Math.Max(1, _processorOptions.HotPathBatchSize);
        var window = _processorOptions.HotPathBatchWindow;
        var reader = _signal.Reader;
        var batch = new List<Guid>(batchSize);

        try
        {
            while (await reader.WaitToReadAsync(ct))
            {
                batch.Clear();

                // First read is mandatory — we wouldn't have unblocked otherwise.
                if (!reader.TryRead(out var first))
                    continue;
                batch.Add(first);
                _hotInFlight.TryAdd(first, 0);

                // Greedy drain of anything already buffered.
                while (batch.Count < batchSize && reader.TryRead(out var id))
                {
                    batch.Add(id);
                    _hotInFlight.TryAdd(id, 0);
                }

                // If the batch isn't full, wait briefly for more arrivals to coalesce. The
                // window bounds dispatch latency at low arrival rates.
                if (batch.Count < batchSize && window > TimeSpan.Zero)
                {
                    using var windowCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    windowCts.CancelAfter(window);
                    try
                    {
                        while (batch.Count < batchSize && await reader.WaitToReadAsync(windowCts.Token))
                        {
                            while (batch.Count < batchSize && reader.TryRead(out var id))
                            {
                                batch.Add(id);
                                _hotInFlight.TryAdd(id, 0);
                            }
                        }
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        // Window elapsed — flush what we have.
                    }
                }

                try
                {
                    if (batch.Count == 1)
                        await _processor.TryProcessByIdAsync(batch[0], ct);
                    else
                        await _processor.TryProcessByIdsAsync(batch, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex,
                        "Hot-path inbox dispatch failed for {Count} message(s)", batch.Count);
                }
                finally
                {
                    foreach (var id in batch)
                        _hotInFlight.TryRemove(id, out _);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private async Task RunColdPathAsync(CancellationToken ct)
    {
        var minInterval = _processorOptions.ColdPollingInterval;
        var maxInterval = _processorOptions.ColdMaxPollingInterval;
        // Guard against misconfiguration (max < min).
        if (maxInterval < minInterval) maxInterval = minInterval;

        var currentInterval = minInterval;

        while (!ct.IsCancellationRequested)
        {
            var processed = 0;
            try
            {
                var skipIds = _hotInFlight.IsEmpty
                    ? null
                    : new HashSet<Guid>(_hotInFlight.Keys);
                processed = await _processor.ProcessBatchAsync(ct, skipIds);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cold-path inbox dispatch batch failed");
            }

            // Adaptive backoff: any work resets to the minimum; idle scans double up to max.
            if (processed > 0)
            {
                currentInterval = minInterval;
            }
            else if (currentInterval < maxInterval)
            {
                var doubledTicks = Math.Min(currentInterval.Ticks * 2, maxInterval.Ticks);
                currentInterval = TimeSpan.FromTicks(Math.Max(doubledTicks, minInterval.Ticks));
            }

            try
            {
                await Task.Delay(currentInterval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
