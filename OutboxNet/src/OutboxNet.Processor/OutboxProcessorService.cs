using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OutboxNet.Interfaces;
using OutboxNet.Options;

namespace OutboxNet.Processor;

public sealed class OutboxProcessorService : BackgroundService
{
    private const int FanoutWarningThreshold = 500;

    private readonly IOutboxProcessor _processor;
    private readonly IOutboxSignal _signal;
    private readonly ProcessorOptions _processorOptions;
    private readonly OutboxOptions _outboxOptions;
    private readonly ILogger<OutboxProcessorService> _logger;

    // IDs currently being processed by the hot path. The cold path skips these so that
    // fresh same-instance messages are handled exclusively by the hot path and the cold path
    // focuses on cross-instance messages, retries, and channel-overflow recovery.
    // This is a same-process optimisation only — the DB lock gate remains the correctness
    // guarantee for multi-instance scenarios.
    private readonly ConcurrentDictionary<Guid, byte> _hotInFlight = new();

    public OutboxProcessorService(
        IOutboxProcessor processor,
        IOutboxSignal signal,
        IOptions<ProcessorOptions> processorOptions,
        IOptions<OutboxOptions> outboxOptions,
        ILogger<OutboxProcessorService> logger)
    {
        _processor = processor;
        _signal = signal;
        _processorOptions = processorOptions.Value;
        _outboxOptions = outboxOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Outbox processor started. Cold polling interval: {Interval}ms",
            _processorOptions.ColdPollingInterval.TotalMilliseconds);

        var fanout = _outboxOptions.BatchSize * _outboxOptions.MaxConcurrentDeliveries
                     * _outboxOptions.MaxConcurrentSubscriptionDeliveries;
        if (fanout >= FanoutWarningThreshold)
        {
            _logger.LogWarning(
                "OutboxNet: BatchSize ({BatchSize}) × MaxConcurrentDeliveries ({MaxMsg}) × " +
                "MaxConcurrentSubscriptionDeliveries ({MaxSub}) = {Fanout} max concurrent HTTP requests. " +
                "Reduce these values if you observe connection-pool or thread-pool exhaustion.",
                _outboxOptions.BatchSize, _outboxOptions.MaxConcurrentDeliveries,
                _outboxOptions.MaxConcurrentSubscriptionDeliveries, fanout);
        }

        await Task.WhenAll(
            RunHotPathAsync(stoppingToken),
            RunColdPathAsync(stoppingToken));
    }

    // Hot path: drain the in-process channel as message IDs arrive.
    // Parallel.ForEachAsync keeps up to MaxConcurrentDeliveries in-flight simultaneously.
    // Each TryProcessByIdAsync does a PK-seek UPDATE — only one instance wins per message ID.
    private async Task RunHotPathAsync(CancellationToken ct)
    {
        try
        {
            await Parallel.ForEachAsync(
                _signal.Reader.ReadAllAsync(ct),
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = _outboxOptions.MaxConcurrentDeliveries,
                    CancellationToken = ct
                },
                async (messageId, token) =>
                {
                    _hotInFlight.TryAdd(messageId, 0);
                    try
                    {
                        await _processor.TryProcessByIdAsync(messageId, token);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError(ex, "Hot-path processing failed for message {MessageId}", messageId);
                    }
                    finally
                    {
                        _hotInFlight.TryRemove(messageId, out _);
                    }
                });
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    // Cold path: fixed-interval scan for cross-instance messages, scheduled retries,
    // and recovery from channel overflow (DropOldest). Skips IDs currently in-flight
    // on the hot path to avoid racing for the same message within this instance.
    private async Task RunColdPathAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Snapshot the in-flight set at call time. ConcurrentDictionary.Keys is a
                // point-in-time view; wrapping it in HashSet<Guid> gives IReadOnlySet<Guid>
                // and an O(1) lookup if the pipeline ever needs Contains().
                var skipIds = _hotInFlight.IsEmpty
                    ? null
                    : new HashSet<Guid>(_hotInFlight.Keys);
                await _processor.ProcessBatchAsync(ct, skipIds);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cold-path outbox processing batch failed");
            }

            try
            {
                await Task.Delay(_processorOptions.ColdPollingInterval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
