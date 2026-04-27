using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Dragonfire.Outbox.Interfaces;
using Dragonfire.Outbox.Models;
using Dragonfire.Outbox.Observability;
using Dragonfire.Outbox.Options;

namespace Dragonfire.Outbox.Processor;

public sealed class OutboxProcessingPipeline : IOutboxProcessor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRetryPolicy _retryPolicy;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxProcessingPipeline> _logger;

    // ReleaseExpiredLocksAsync is an UPDATE across the entire table; throttle it so that
    // zero-delay hot-path polling (saturated queue) doesn't hammer it every cycle.
    private static readonly TimeSpan ReleaseExpiredLocksInterval = TimeSpan.FromSeconds(30);
    // Stored as UTC ticks so Interlocked.Read/Exchange can be used for thread safety.
    // Azure Functions may invoke ProcessBatchAsync concurrently from multiple timer firings.
    private long _lastLockReleaseTicks = DateTimeOffset.MinValue.UtcTicks;

    public OutboxProcessingPipeline(
        IServiceScopeFactory scopeFactory,
        IRetryPolicy retryPolicy,
        IOptions<OutboxOptions> options,
        ILogger<OutboxProcessingPipeline> logger)
    {
        _scopeFactory = scopeFactory;
        _retryPolicy = retryPolicy;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int> ProcessBatchAsync(CancellationToken ct = default, IReadOnlySet<Guid>? skipIds = null)
    {
        using var activity = OutboxActivitySource.Source.StartActivity("outbox.process_batch");
        var batchStopwatch = Stopwatch.StartNew();
        var lockedBy = _options.InstanceId;

        try
        {
            using var batchScope = _scopeFactory.CreateScope();
            var sp = batchScope.ServiceProvider;
            var outboxStore = sp.GetRequiredService<IOutboxStore>();
            var messagePublisher = sp.GetService<IMessagePublisher>();

            // Throttled: only run ReleaseExpiredLocksAsync once every 30 s.
            // Use Interlocked so concurrent Azure Functions invocations don't both fire it.
            var nowTicks = DateTimeOffset.UtcNow.UtcTicks;
            var lastTicks = Interlocked.Read(ref _lastLockReleaseTicks);
            if (nowTicks - lastTicks >= ReleaseExpiredLocksInterval.Ticks
                && Interlocked.CompareExchange(ref _lastLockReleaseTicks, nowTicks, lastTicks) == lastTicks)
            {
                await outboxStore.ReleaseExpiredLocksAsync(ct);
            }

            var messages = await outboxStore.LockNextBatchAsync(
                _options.BatchSize,
                _options.DefaultVisibilityTimeout,
                lockedBy,
                skipIds,
                ct);

            if (messages.Count == 0)
                return 0;

            OutboxMetrics.BatchesProcessed.Add(1);
            OutboxMetrics.BatchSize.Record(messages.Count);
            activity?.SetTag("outbox.batch_size", messages.Count);

            _logger.LogInformation("Processing batch of {Count} outbox messages", messages.Count);

            if (_options.ProcessingMode == ProcessingMode.QueueMediated && messagePublisher is not null)
            {
                await ProcessQueueMediatedAsync(messages, lockedBy, outboxStore, messagePublisher, ct);
            }
            else
            {
                // Pre-fetch subscriptions once per unique (EventType, TenantId) pair in this batch.
                // Reduces N per-message DB queries down to at most U unique routing pairs.
                var subCache = await BuildSubscriptionCacheAsync(
                    messages, sp.GetRequiredService<ISubscriptionReader>(), ct);

                await ProcessDirectDeliveryAsync(messages, lockedBy, subCache, ct);
            }

            return messages.Count;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error processing outbox batch");
            throw;
        }
        finally
        {
            batchStopwatch.Stop();
            OutboxMetrics.ProcessingDuration.Record(batchStopwatch.Elapsed.TotalMilliseconds);
        }
    }

    public async Task<bool> TryProcessByIdAsync(Guid messageId, CancellationToken ct = default)
    {
        var lockedBy = _options.InstanceId;

        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var outboxStore = sp.GetRequiredService<IOutboxStore>();

        var message = await outboxStore.TryLockByIdAsync(
            messageId, _options.DefaultVisibilityTimeout, lockedBy, ct);

        if (message is null)
            return false;

        IReadOnlyList<WebhookSubscription> subscriptions;
        if (_options.ProcessingMode == ProcessingMode.QueueMediated)
        {
            var messagePublisher = sp.GetService<IMessagePublisher>();
            if (messagePublisher is not null)
                await ProcessQueueMediatedAsync([message], lockedBy, outboxStore, messagePublisher, ct);
            return true;
        }

        subscriptions = await sp.GetRequiredService<ISubscriptionReader>().GetForMessageAsync(message, ct);

        await ProcessSingleMessageAsync(
            message,
            lockedBy,
            subscriptions,
            outboxStore,
            sp.GetRequiredService<IDeliveryAttemptStore>(),
            sp.GetRequiredService<IWebhookDeliverer>(),
            ct);

        return true;
    }

    // ── Subscription cache ────────────────────────────────────────────────────

    private static async Task<Dictionary<(string EventType, string? TenantId), IReadOnlyList<WebhookSubscription>>>
        BuildSubscriptionCacheAsync(
            IReadOnlyList<OutboxMessage> messages,
            ISubscriptionReader reader,
            CancellationToken ct)
    {
        var cache = new Dictionary<(string, string?), IReadOnlyList<WebhookSubscription>>();

        foreach (var message in messages)
        {
            var key = (message.EventType, message.TenantId);
            if (!cache.ContainsKey(key))
                cache[key] = await reader.GetForMessageAsync(message, ct);
        }

        return cache;
    }

    // ── Direct delivery ───────────────────────────────────────────────────────

    private async Task ProcessDirectDeliveryAsync(
        IReadOnlyList<OutboxMessage> messages,
        string lockedBy,
        Dictionary<(string EventType, string? TenantId), IReadOnlyList<WebhookSubscription>> subCache,
        CancellationToken ct)
    {
        await Parallel.ForEachAsync(
            messages,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _options.MaxConcurrentDeliveries,
                CancellationToken = ct
            },
            async (message, token) =>
            {
                // Each concurrent task gets its own DI scope so that scoped services
                // (EfCoreOutboxStore / OutboxDbContext) are not shared across threads.
                using var messageScope = _scopeFactory.CreateScope();
                var msp = messageScope.ServiceProvider;

                var subscriptions = subCache[(message.EventType, message.TenantId)];

                await ProcessSingleMessageAsync(
                    message,
                    lockedBy,
                    subscriptions,
                    msp.GetRequiredService<IOutboxStore>(),
                    msp.GetRequiredService<IDeliveryAttemptStore>(),
                    msp.GetRequiredService<IWebhookDeliverer>(),
                    token);
            });
    }

    // ── Queue-mediated delivery ───────────────────────────────────────────────

    private async Task ProcessQueueMediatedAsync(
        IReadOnlyList<OutboxMessage> messages,
        string lockedBy,
        IOutboxStore outboxStore,
        IMessagePublisher messagePublisher,
        CancellationToken ct)
    {
        foreach (var message in messages)
        {
            try
            {
                await messagePublisher.PublishAsync(message, ct);

                if (await outboxStore.MarkAsProcessedAsync(message.Id, lockedBy, ct))
                {
                    OutboxMetrics.MessagesProcessed.Add(1,
                        new KeyValuePair<string, object?>("event_type", message.EventType));
                }
                else
                {
                    _logger.LogWarning(
                        "Lock lost for message {MessageId} after queue publish (duplicate possible)",
                        message.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish message {MessageId} to queue", message.Id);
                await HandleMessageFailureAsync(message, lockedBy, outboxStore, ex.Message, ct);
            }
        }
    }

    // ── Single-message processing ─────────────────────────────────────────────

    private async Task ProcessSingleMessageAsync(
        OutboxMessage message,
        string lockedBy,
        IReadOnlyList<WebhookSubscription> subscriptions,
        IOutboxStore outboxStore,
        IDeliveryAttemptStore attemptStore,
        IWebhookDeliverer deliverer,
        CancellationToken ct)
    {
        if (subscriptions.Count == 0)
        {
            _logger.LogDebug(
                "No active subscriptions for event type {EventType}, marking as processed",
                message.EventType);
            await outboxStore.MarkAsProcessedAsync(message.Id, lockedBy, ct);
            OutboxMetrics.MessagesProcessed.Add(1,
                new KeyValuePair<string, object?>("event_type", message.EventType));
            return;
        }

        // Fetch all prior delivery states in ONE query (one GROUP BY round-trip).
        var subIds = subscriptions.Select(s => s.Id).ToList();
        IReadOnlyDictionary<Guid, SubscriptionDeliveryState> states;
        try
        {
            states = await attemptStore.GetDeliveryStatesAsync(message.Id, subIds, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to fetch delivery states for message {MessageId}", message.Id);
            await HandleMessageFailureAsync(message, lockedBy, outboxStore, ex.Message, ct);
            return;
        }

        // ── DELIVERY PHASE ─────────────────────────────────────────────────────
        // Errors in this phase are genuine delivery failures → schedule global retry.

        DeliveryOutcome outcome;

        try
        {
            outcome = await DeliverToSubscriptionsAsync(
                message, subscriptions, states, deliverer, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Delivery phase threw unexpectedly (e.g. deliverer threw rather than returning
            // a failure result). Try to persist any attempts that were already recorded before
            // the exception, then schedule a retry.
            _logger.LogError(ex, "Unexpected error during delivery phase for message {MessageId}", message.Id);

            // outcome is unassigned if DeliverToSubscriptionsAsync threw; nothing to save.
            await TrySaveAttemptsAsync(attemptStore, [], message.Id, ct);
            await HandleMessageFailureAsync(message, lockedBy, outboxStore, ex.Message, ct);
            return;
        }

        var (successCount, failedCount, exhaustedCount, lastError, newAttempts) = outcome;

        // ── BOOKKEEPING PHASE ──────────────────────────────────────────────────
        // Errors here must NOT schedule a global retry if any delivery already succeeded —
        // that would cause duplicate delivery. Instead we try to save the success record
        // persistently before deciding what to do with the message.

        if (newAttempts.Count > 0)
        {
            var saved = await TrySaveAttemptsAsync(attemptStore, newAttempts, message.Id, ct);

            if (!saved && successCount > 0)
            {
                // Could not persist the success record. We cannot mark the message as
                // Delivered without the record (another instance would re-deliver), and we
                // cannot retry without risk of re-delivering again. The safest choice:
                // leave the message locked — it will expire and be re-queued. On that retry
                // GetDeliveryStatesAsync will still return no success (record was lost), so
                // duplicate delivery is possible. Log a critical alert so operators know.
                _logger.LogCritical(
                    "CRITICAL: Could not persist delivery success record for message {MessageId}. " +
                    "The message will be retried after lock expiry and MAY be re-delivered. " +
                    "Webhook consumers MUST be idempotent on X-Outbox-Message-Id + X-Outbox-Subscription-Id.",
                    message.Id);
                // Do NOT call HandleMessageFailureAsync here — that would increment the global
                // retry counter, scheduling an immediate retry that will definitely re-deliver.
                // Better to let the visibility timeout expire naturally (5 min), giving the DB
                // time to recover, before another instance retries.
                return;
            }
        }

        // ── DECISION PHASE ─────────────────────────────────────────────────────
        try
        {
            var total = subscriptions.Count;

            if (failedCount == 0 && exhaustedCount == 0)
            {
                // All subscriptions succeeded (now or previously).
                if (await outboxStore.MarkAsProcessedAsync(message.Id, lockedBy, ct))
                {
                    OutboxMetrics.MessagesProcessed.Add(1,
                        new KeyValuePair<string, object?>("event_type", message.EventType));
                }
                else
                {
                    // Lock was stolen AFTER successful delivery. The success records ARE saved
                    // (bookkeeping phase succeeded), so any other instance that picks this up
                    // will call GetDeliveryStatesAsync, find HasSuccess=true, skip delivery,
                    // and mark processed. No duplicate will occur.
                    _logger.LogWarning(
                        "Lock lost for message {MessageId} after successful delivery " +
                        "(success records saved — no re-delivery will occur)",
                        message.Id);
                }
            }
            else if (failedCount > 0)
            {
                await HandleMessageFailureAsync(
                    message, lockedBy, outboxStore,
                    lastError ?? "One or more deliveries failed", ct);
            }
            else
            {
                // exhaustedCount > 0, failedCount == 0 — every subscription hit its per-sub
                // limit without ever succeeding. Dead-letter so it doesn't spin forever.
                _logger.LogError(
                    "Message {MessageId}: {Exhausted}/{Total} subscriptions exhausted all retries without success — dead-lettering",
                    message.Id, exhaustedCount, total);

                if (await outboxStore.MarkAsDeadLetteredAsync(message.Id, lockedBy, ct))
                {
                    OutboxMetrics.MessagesDeadLettered.Add(1,
                        new KeyValuePair<string, object?>("event_type", message.EventType));
                }
                else
                {
                    _logger.LogWarning(
                        "Lock lost for message {MessageId} during dead-letter handling",
                        message.Id);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error updating message status for {MessageId}", message.Id);
            // Don't re-throw; the lock will expire and the message will be retried.
        }
    }

    // ── Parallel subscription delivery ────────────────────────────────────────

    private sealed record DeliveryOutcome(
        int SuccessCount,
        int FailedCount,
        int ExhaustedCount,
        string? LastError,
        List<DeliveryAttempt> NewAttempts);

    private async Task<DeliveryOutcome> DeliverToSubscriptionsAsync(
        OutboxMessage message,
        IReadOnlyList<WebhookSubscription> subscriptions,
        IReadOnlyDictionary<Guid, SubscriptionDeliveryState> states,
        IWebhookDeliverer deliverer,
        CancellationToken ct)
    {
        // Thread-safe accumulators for parallel delivery.
        var successCountLocal = 0;
        var failedCountLocal  = 0;
        var exhaustedCountLocal = 0;
        string? lastErrorLocal = null;
        var attemptsLock = new object();
        var localNewAttempts = new List<DeliveryAttempt>(subscriptions.Count);

        await Parallel.ForEachAsync(
            subscriptions,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _options.MaxConcurrentSubscriptionDeliveries,
                CancellationToken = ct
            },
            async (subscription, token) =>
            {
                states.TryGetValue(subscription.Id, out var state);
                var attemptCount = state?.AttemptCount ?? 0;
                var hasSuccess   = state?.HasSuccess   ?? false;

                if (hasSuccess)
                {
                    Interlocked.Increment(ref successCountLocal);
                    return;
                }

                if (attemptCount > subscription.MaxRetries)
                {
                    Interlocked.Increment(ref exhaustedCountLocal);
                    _logger.LogWarning(
                        "Subscription {SubscriptionId} exhausted {Max} retries for message {MessageId}",
                        subscription.Id, subscription.MaxRetries, message.Id);
                    return;
                }

                // Derive a deterministic delivery ID from (MessageId, SubscriptionId, AttemptNumber).
                // This allows webhook consumers to deduplicate retries of the same attempt using
                // X-Outbox-Delivery-Id as an idempotency key. A new attempt number produces a
                // different ID, which is correct (it IS a different delivery attempt).
                var deliveryId = DeriveDeliveryId(message.Id, subscription.Id, attemptCount + 1);

                var result = await deliverer.DeliverAsync(message, subscription, deliveryId, token);

                var attempt = new DeliveryAttempt
                {
                    Id = deliveryId,   // same as the header — stored for correlation
                    OutboxMessageId = message.Id,
                    WebhookSubscriptionId = subscription.Id,
                    AttemptNumber = attemptCount + 1,
                    Status = result.Success ? DeliveryStatus.Success : DeliveryStatus.Failed,
                    HttpStatusCode = result.HttpStatusCode,
                    ResponseBody = result.ResponseBody,
                    ErrorMessage = result.ErrorMessage,
                    DurationMs = result.DurationMs,
                    AttemptedAt = DateTimeOffset.UtcNow
                };

                lock (attemptsLock)
                    localNewAttempts.Add(attempt);

                if (result.Success)
                    Interlocked.Increment(ref successCountLocal);
                else
                {
                    Interlocked.Increment(ref failedCountLocal);
                    Volatile.Write(ref lastErrorLocal, result.ErrorMessage);
                }
            });

        return new DeliveryOutcome(
            successCountLocal,
            failedCountLocal,
            exhaustedCountLocal,
            lastErrorLocal,
            localNewAttempts);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Derives a stable <see cref="Guid"/> from (messageId, subscriptionId, attemptNumber).
    /// The same triple always produces the same ID, making it usable as an idempotency key.
    /// </summary>
    private static Guid DeriveDeliveryId(Guid messageId, Guid subscriptionId, int attemptNumber)
    {
        // Stack-allocate the input: 16 (message) + 16 (sub) + 4 (attempt) = 36 bytes.
        Span<byte> input = stackalloc byte[36];
        messageId.TryWriteBytes(input);
        subscriptionId.TryWriteBytes(input[16..]);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(input[32..], attemptNumber);

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);

        // Force RFC 4122 version 5 (name-based SHA-1 UUID variant) layout bits.
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50); // version = 5
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // variant = RFC 4122

        return new Guid(hash[..16]);
    }

    /// <summary>
    /// Attempts to save delivery attempts, logging on failure but not throwing.
    /// Returns true if the save succeeded (or there were no attempts to save).
    /// </summary>
    private async Task<bool> TrySaveAttemptsAsync(
        IDeliveryAttemptStore store,
        List<DeliveryAttempt> attempts,
        Guid messageId,
        CancellationToken ct)
    {
        if (attempts.Count == 0) return true;

        try
        {
            await store.SaveAttemptsAsync(attempts, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Failed to save {Count} delivery attempt(s) for message {MessageId}",
                attempts.Count, messageId);
            return false;
        }
    }

    private async Task HandleMessageFailureAsync(
        OutboxMessage message,
        string lockedBy,
        IOutboxStore outboxStore,
        string error,
        CancellationToken ct)
    {
        // message.RetryCount is a snapshot from LockNextBatchAsync; use it as a
        // best-effort value for computing delay. The DB value is the authoritative
        // count and is incremented atomically by IncrementRetryAsync.
        var nextDelay = _retryPolicy.GetNextDelay(message.RetryCount);

        if (nextDelay.HasValue)
        {
            var nextRetryAt = DateTimeOffset.UtcNow.Add(nextDelay.Value);

            if (await outboxStore.IncrementRetryAsync(message.Id, lockedBy, nextRetryAt, error, ct))
            {
                OutboxMetrics.MessagesFailed.Add(1,
                    new KeyValuePair<string, object?>("event_type", message.EventType));

                _logger.LogWarning(
                    "Message {MessageId} delivery failed, scheduled retry at {NextRetryAt}",
                    message.Id, nextRetryAt);
            }
            else
            {
                _logger.LogWarning("Lock lost for message {MessageId} during failure handling", message.Id);
            }
        }
        else
        {
            if (await outboxStore.MarkAsDeadLetteredAsync(message.Id, lockedBy, ct))
            {
                OutboxMetrics.MessagesDeadLettered.Add(1,
                    new KeyValuePair<string, object?>("event_type", message.EventType));

                _logger.LogError("Message {MessageId} exhausted global retries, moved to dead letter", message.Id);
            }
            else
            {
                _logger.LogWarning(
                    "Lock lost for message {MessageId} during dead-letter handling",
                    message.Id);
            }
        }
    }
}
