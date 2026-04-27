using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using InboxNet.Dispatch;
using InboxNet.Interfaces;
using InboxNet.Models;
using InboxNet.Observability;
using InboxNet.Options;

namespace InboxNet.Processor;

/// <summary>
/// Inbox dispatch pipeline. Locks messages, resolves matching handlers, runs them sequentially
/// in registration order, records per-handler attempts, and decides the message's final
/// disposition. Handlers are sequential (unlike the outbox's parallel subscription delivery)
/// because inbox handlers often share a logical entity and ordering matters.
/// </summary>
/// <remarks>
/// Three knobs on <see cref="InboxOptions"/> control the bookkeeping cost on the happy path:
/// <list type="bullet">
///   <item><see cref="InboxOptions.BulkBookkeeping"/> — flush attempts and processed-status
///   updates once per batch instead of per message.</item>
///   <item><see cref="InboxOptions.RecordAttemptsOnSuccess"/> — when false, only failures
///   leave an attempt row.</item>
///   <item><see cref="InboxOptions.RecordHandlerAttempts"/> — when false, skip the attempt
///   store entirely (no SELECT, no INSERTs); handlers run on every dispatch and must be
///   idempotent on <see cref="InboxMessage.Id"/>.</item>
/// </list>
/// </remarks>
public sealed class InboxProcessingPipeline : IInboxProcessor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IInboxHandlerRegistry _handlerRegistry;
    private readonly IInboxRetryPolicy _retryPolicy;
    private readonly InboxOptions _options;
    private readonly InboxProcessorOptions _processorOptions;
    private readonly ILogger<InboxProcessingPipeline> _logger;

    // ReleaseExpiredLocksAsync is a table-wide UPDATE; throttle so a saturated cold path
    // doesn't trigger it on every batch. The interval is configurable via
    // InboxProcessorOptions.ReleaseExpiredLocksInterval.
    private long _lastLockReleaseTicks = DateTimeOffset.MinValue.UtcTicks;

    public InboxProcessingPipeline(
        IServiceScopeFactory scopeFactory,
        IInboxHandlerRegistry handlerRegistry,
        IInboxRetryPolicy retryPolicy,
        IOptions<InboxOptions> options,
        IOptions<InboxProcessorOptions> processorOptions,
        ILogger<InboxProcessingPipeline> logger)
    {
        _scopeFactory = scopeFactory;
        _handlerRegistry = handlerRegistry;
        _retryPolicy = retryPolicy;
        _options = options.Value;
        _processorOptions = processorOptions.Value;
        _logger = logger;
    }

    public async Task<int> ProcessBatchAsync(CancellationToken ct = default, IReadOnlySet<Guid>? skipIds = null)
    {
        using var activity = InboxActivitySource.Source.StartActivity("inbox.process_batch");
        var batchStopwatch = Stopwatch.StartNew();
        var lockedBy = _options.InstanceId;

        try
        {
            using var batchScope = _scopeFactory.CreateScope();
            var sp = batchScope.ServiceProvider;
            var store = sp.GetRequiredService<IInboxStore>();

            await MaybeReleaseExpiredLocksAsync(store, ct);

            var messages = await store.LockNextBatchAsync(
                _options.BatchSize,
                _options.DefaultVisibilityTimeout,
                lockedBy,
                skipIds,
                ct);

            if (messages.Count == 0)
                return 0;

            activity?.SetTag("inbox.batch_size", messages.Count);
            _logger.LogInformation("Processing batch of {Count} inbox messages", messages.Count);

            return await DispatchAndFlushAsync(messages, lockedBy, sp, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error processing inbox batch");
            throw;
        }
        finally
        {
            batchStopwatch.Stop();
            InboxMetrics.ProcessingDuration.Record(batchStopwatch.Elapsed.TotalMilliseconds);
        }
    }

    public async Task<bool> TryProcessByIdAsync(Guid messageId, CancellationToken ct = default)
    {
        var lockedBy = _options.InstanceId;

        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var store = sp.GetRequiredService<IInboxStore>();

        var message = await store.TryLockByIdAsync(
            messageId, _options.DefaultVisibilityTimeout, lockedBy, ct);

        if (message is null)
            return false;

        // Single-message path always flushes immediately — there's nothing to amortize over.
        var bookkeeper = new BatchBookkeeper();
        await DispatchMessageAsync(message, lockedBy, sp, bookkeeper, ct);
        await FlushBatchAsync(bookkeeper, lockedBy, sp, ct);
        return true;
    }

    public async Task<int> TryProcessByIdsAsync(IReadOnlyCollection<Guid> messageIds, CancellationToken ct = default)
    {
        if (messageIds.Count == 0) return 0;

        using var activity = InboxActivitySource.Source.StartActivity("inbox.process_by_ids");
        var batchStopwatch = Stopwatch.StartNew();
        var lockedBy = _options.InstanceId;

        try
        {
            using var batchScope = _scopeFactory.CreateScope();
            var sp = batchScope.ServiceProvider;
            var store = sp.GetRequiredService<IInboxStore>();

            var messages = await store.LockByIdsAsync(
                messageIds,
                _options.DefaultVisibilityTimeout,
                lockedBy,
                ct);

            if (messages.Count == 0)
                return 0;

            activity?.SetTag("inbox.batch_size", messages.Count);

            return await DispatchAndFlushAsync(messages, lockedBy, sp, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error processing inbox batch by ids ({Count} requested)", messageIds.Count);
            throw;
        }
        finally
        {
            batchStopwatch.Stop();
            InboxMetrics.ProcessingDuration.Record(batchStopwatch.Elapsed.TotalMilliseconds);
        }
    }

    // ── Shared dispatch+flush ─────────────────────────────────────────────────

    private async Task<int> DispatchAndFlushAsync(
        IReadOnlyList<InboxMessage> messages,
        string lockedBy,
        IServiceProvider sp,
        CancellationToken ct)
    {
        InboxMetrics.BatchesProcessed.Add(1);
        InboxMetrics.BatchSize.Record(messages.Count);

        var bookkeeper = new BatchBookkeeper();

        if (messages.Count == 1)
        {
            // No point spinning up Parallel infrastructure for a single message.
            await DispatchMessageAsync(messages[0], lockedBy, sp, bookkeeper, ct);
        }
        else
        {
            await Parallel.ForEachAsync(
                messages,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = _options.MaxConcurrentDispatch,
                    CancellationToken = ct
                },
                async (message, token) =>
                {
                    using var messageScope = _scopeFactory.CreateScope();
                    var msp = messageScope.ServiceProvider;

                    await DispatchMessageAsync(
                        message,
                        lockedBy,
                        msp,
                        bookkeeper,
                        token);
                });
        }

        await FlushBatchAsync(bookkeeper, lockedBy, sp, ct);
        return messages.Count;
    }

    private async Task MaybeReleaseExpiredLocksAsync(IInboxStore store, CancellationToken ct)
    {
        var nowTicks = DateTimeOffset.UtcNow.UtcTicks;
        var lastTicks = Interlocked.Read(ref _lastLockReleaseTicks);
        if (nowTicks - lastTicks >= _processorOptions.ReleaseExpiredLocksInterval.Ticks
            && Interlocked.CompareExchange(ref _lastLockReleaseTicks, nowTicks, lastTicks) == lastTicks)
        {
            await store.ReleaseExpiredLocksAsync(ct);
        }
    }

    // ── Core dispatch ─────────────────────────────────────────────────────────

    private async Task DispatchMessageAsync(
        InboxMessage message,
        string lockedBy,
        IServiceProvider sp,
        BatchBookkeeper bookkeeper,
        CancellationToken ct)
    {
        using var activity = InboxActivitySource.Source.StartActivity("inbox.dispatch");
        activity?.SetTag("inbox.message_id", message.Id.ToString());
        activity?.SetTag("inbox.provider_key", message.ProviderKey);
        activity?.SetTag("inbox.event_type", message.EventType);

        var matching = _handlerRegistry.GetMatching(message.ProviderKey, message.EventType);

        if (matching.Count == 0)
        {
            _logger.LogDebug(
                "No handlers registered for provider {ProviderKey}, event {EventType} — marking message {MessageId} as processed",
                message.ProviderKey, message.EventType, message.Id);

            bookkeeper.AddSuccess(message);
            return;
        }

        IReadOnlyDictionary<string, HandlerAttemptState> states;
        if (_options.RecordHandlerAttempts)
        {
            try
            {
                var attemptStore = sp.GetRequiredService<IInboxHandlerAttemptStore>();
                var handlerNames = matching.Select(r => r.HandlerName).ToList();
                states = await attemptStore.GetHandlerStatesAsync(message.Id, handlerNames, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to fetch handler states for message {MessageId}", message.Id);
                bookkeeper.AddFailure(message, ex.Message);
                return;
            }
        }
        else
        {
            // Attempt tracking disabled: pretend no prior attempts exist. Handlers re-run on
            // every dispatch — this contract is documented on InboxOptions.RecordHandlerAttempts.
            states = EmptyStates;
        }

        // ── DISPATCH PHASE ─────────────────────────────────────────────────────
        // Handlers run sequentially in registration order. If one fails we stop and let the
        // retry cycle pick up where we left off — successful handlers won't re-run because
        // their success records are persisted.
        var outcome = await RunHandlersAsync(message, matching, states, sp, ct);

        // ── BOOKKEEPING PHASE ──────────────────────────────────────────────────
        // Filter attempts according to the configured policy before publishing into the
        // bookkeeper. The flush stage is responsible for the actual round-trip.
        if (_options.RecordHandlerAttempts && outcome.NewAttempts.Count > 0)
        {
            foreach (var attempt in outcome.NewAttempts)
            {
                var keep = attempt.Status != InboxHandlerStatus.Success
                           || _options.RecordAttemptsOnSuccess;
                if (keep) bookkeeper.Attempts.Add(attempt);
            }
        }

        // ── DECISION PHASE ─────────────────────────────────────────────────────
        if (outcome.FailedCount == 0 && outcome.ExhaustedCount == 0)
        {
            bookkeeper.AddSuccess(message);
        }
        else if (outcome.FailedCount > 0)
        {
            bookkeeper.AddFailure(message, outcome.LastError ?? "One or more handlers failed");
        }
        else
        {
            // All remaining handlers are exhausted — dead-letter the message so it doesn't spin.
            _logger.LogError(
                "Message {MessageId}: {Exhausted} handler(s) exhausted retries — dead-lettering",
                message.Id, outcome.ExhaustedCount);
            bookkeeper.AddDeadLetter(message);
        }
    }

    // ── Flush ─────────────────────────────────────────────────────────────────

    private async Task FlushBatchAsync(
        BatchBookkeeper bookkeeper,
        string lockedBy,
        IServiceProvider sp,
        CancellationToken ct)
    {
        var store = sp.GetRequiredService<IInboxStore>();

        // 1. Persist attempts. With BulkBookkeeping=true the SaveAttempts call is one INSERT
        //    set; with BulkBookkeeping=false we partition by message and write each separately
        //    so a partial failure only stalls one message's bookkeeping.
        if (_options.RecordHandlerAttempts && bookkeeper.Attempts.Count > 0)
        {
            var attemptStore = sp.GetRequiredService<IInboxHandlerAttemptStore>();

            // ConcurrentBag enumeration is snapshot-safe but we need a list for the store API.
            var allAttempts = bookkeeper.Attempts.ToList();

            if (_options.BulkBookkeeping)
            {
                if (!await TrySaveAttemptsAsync(attemptStore, allAttempts, ct))
                {
                    // Bulk attempt write failed. Treat every message that had a success as
                    // "leave the lock to expire" — same invariant as the per-message path.
                    var hasAnySuccess = allAttempts.Any(a => a.Status == InboxHandlerStatus.Success);
                    if (hasAnySuccess)
                    {
                        _logger.LogCritical(
                            "CRITICAL: Bulk attempt save failed for {Count} attempt(s). " +
                            "Successful handlers MAY re-run after lock expiry — handlers MUST be " +
                            "idempotent on InboxMessage.Id.",
                            allAttempts.Count);
                        return;
                    }
                }
            }
            else
            {
                var byMessage = allAttempts.GroupBy(a => a.InboxMessageId);
                foreach (var group in byMessage)
                {
                    var attempts = group.ToList();
                    if (!await TrySaveAttemptsAsync(attemptStore, attempts, ct))
                    {
                        var hasSuccess = attempts.Any(a => a.Status == InboxHandlerStatus.Success);
                        if (hasSuccess)
                        {
                            _logger.LogCritical(
                                "CRITICAL: Could not persist handler attempt records for message {MessageId}. " +
                                "The message will be retried after lock expiry and handlers that succeeded MAY re-run. " +
                                "Inbox handlers MUST be idempotent on InboxMessage.Id.",
                                group.Key);
                            // Drop this message from the success list so we don't mark it processed.
                            bookkeeper.SuppressSuccess(group.Key);
                        }
                    }
                }
            }
        }

        // 2. Mark successful messages as processed.
        if (bookkeeper.SuccessIds.Count > 0)
        {
            try
            {
                if (_options.BulkBookkeeping)
                {
                    var ids = bookkeeper.SuccessIds.ToArray();
                    var updated = await store.MarkAsProcessedBulkAsync(ids, lockedBy, ct);
                    if (updated < ids.Length)
                    {
                        _logger.LogWarning(
                            "Lock lost for {Lost}/{Total} message(s) during bulk mark-as-processed " +
                            "(success records saved — no re-dispatch will occur).",
                            ids.Length - updated, ids.Length);
                    }
                    foreach (var (provider, eventType) in bookkeeper.SuccessTags)
                    {
                        InboxMetrics.MessagesProcessed.Add(1,
                            new KeyValuePair<string, object?>("provider", provider),
                            new KeyValuePair<string, object?>("event_type", eventType));
                    }
                }
                else
                {
                    foreach (var (id, provider, eventType) in bookkeeper.EnumerateSuccessRecords())
                    {
                        if (await store.MarkAsProcessedAsync(id, lockedBy, ct))
                        {
                            InboxMetrics.MessagesProcessed.Add(1,
                                new KeyValuePair<string, object?>("provider", provider),
                                new KeyValuePair<string, object?>("event_type", eventType));
                        }
                        else
                        {
                            _logger.LogWarning(
                                "Lock lost for message {MessageId} after successful dispatch " +
                                "(success records saved — no re-dispatch will occur)",
                                id);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error marking {Count} message(s) as processed", bookkeeper.SuccessIds.Count);
            }
        }

        // 3. Failure handling. Each failure has its own retry schedule and error string;
        //    BulkBookkeeping=true coalesces the schedules into one OPENJSON-driven UPDATE.
        //    Failures whose retry policy returned null are routed to dead-letter alongside
        //    the bookkeeper.DeadLetters set. We pair each schedule with its source message
        //    so the bulk path can emit per-message metrics without an O(N²) lookup.
        var failureSnapshot = bookkeeper.Failures.ToList();
        var policyDeadLetters = new List<InboxMessage>();
        if (failureSnapshot.Count > 0)
        {
            var pendingRetries = new List<(InboxMessage Message, InboxRetrySchedule Schedule)>(failureSnapshot.Count);
            foreach (var (message, error) in failureSnapshot)
            {
                var nextDelay = _retryPolicy.GetNextDelay(message.RetryCount);
                if (nextDelay.HasValue)
                {
                    var nextRetryAt = DateTimeOffset.UtcNow.Add(nextDelay.Value);
                    pendingRetries.Add((message, new InboxRetrySchedule(message.Id, nextRetryAt, error)));
                }
                else
                {
                    policyDeadLetters.Add(message);
                }
            }

            if (pendingRetries.Count > 0)
            {
                try
                {
                    if (_options.BulkBookkeeping)
                    {
                        var schedules = pendingRetries.Select(p => p.Schedule).ToList();
                        var updated = await store.IncrementRetryBulkAsync(schedules, lockedBy, ct);
                        if (updated < schedules.Count)
                        {
                            _logger.LogWarning(
                                "Lock lost for {Lost}/{Total} message(s) during bulk retry-scheduling.",
                                schedules.Count - updated, schedules.Count);
                        }
                        foreach (var (msg, _) in pendingRetries)
                        {
                            InboxMetrics.MessagesFailed.Add(1,
                                new KeyValuePair<string, object?>("provider", msg.ProviderKey),
                                new KeyValuePair<string, object?>("event_type", msg.EventType));
                        }
                    }
                    else
                    {
                        foreach (var (msg, sched) in pendingRetries)
                        {
                            if (await store.IncrementRetryAsync(sched.MessageId, lockedBy, sched.NextRetryAt, sched.Error, ct))
                            {
                                InboxMetrics.MessagesFailed.Add(1,
                                    new KeyValuePair<string, object?>("provider", msg.ProviderKey),
                                    new KeyValuePair<string, object?>("event_type", msg.EventType));
                            }
                            else
                            {
                                _logger.LogWarning("Lock lost for message {MessageId} during failure handling", sched.MessageId);
                            }
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Error scheduling retries for {Count} failure(s)", pendingRetries.Count);
                }
            }
        }

        // 4. Dead-lettering — handler-exhausted messages plus retry-policy-exhausted messages.
        var allDeadLetters = bookkeeper.DeadLetters.ToList();
        allDeadLetters.AddRange(policyDeadLetters);
        if (allDeadLetters.Count > 0)
        {
            try
            {
                if (_options.BulkBookkeeping)
                {
                    var ids = allDeadLetters.Select(m => m.Id).ToArray();
                    var updated = await store.MarkAsDeadLetteredBulkAsync(ids, lockedBy, ct);
                    if (updated < ids.Length)
                    {
                        _logger.LogWarning(
                            "Lock lost for {Lost}/{Total} message(s) during bulk dead-letter.",
                            ids.Length - updated, ids.Length);
                    }
                    foreach (var msg in allDeadLetters)
                    {
                        InboxMetrics.MessagesDeadLettered.Add(1,
                            new KeyValuePair<string, object?>("provider", msg.ProviderKey),
                            new KeyValuePair<string, object?>("event_type", msg.EventType));
                    }
                }
                else
                {
                    foreach (var msg in allDeadLetters)
                    {
                        if (await store.MarkAsDeadLetteredAsync(msg.Id, lockedBy, ct))
                        {
                            InboxMetrics.MessagesDeadLettered.Add(1,
                                new KeyValuePair<string, object?>("provider", msg.ProviderKey),
                                new KeyValuePair<string, object?>("event_type", msg.EventType));
                        }
                        else
                        {
                            _logger.LogWarning("Lock lost for message {MessageId} during dead-letter handling", msg.Id);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error dead-lettering {Count} message(s)", allDeadLetters.Count);
            }
        }
    }

    // ── Sequential handler dispatch ───────────────────────────────────────────

    private static readonly IReadOnlyDictionary<string, HandlerAttemptState> EmptyStates =
        new Dictionary<string, HandlerAttemptState>();

    private sealed record DispatchOutcome(
        int SuccessCount,
        int FailedCount,
        int ExhaustedCount,
        string? LastError,
        List<InboxHandlerAttempt> NewAttempts);

    private async Task<DispatchOutcome> RunHandlersAsync(
        InboxMessage message,
        IReadOnlyList<InboxHandlerRegistration> handlers,
        IReadOnlyDictionary<string, HandlerAttemptState> states,
        IServiceProvider sp,
        CancellationToken ct)
    {
        var successCount = 0;
        var failedCount = 0;
        var exhaustedCount = 0;
        string? lastError = null;
        var newAttempts = new List<InboxHandlerAttempt>(handlers.Count);

        foreach (var reg in handlers)
        {
            states.TryGetValue(reg.HandlerName, out var state);
            var attemptCount = state?.AttemptCount ?? 0;
            var hasSuccess = state?.HasSuccess ?? false;

            if (hasSuccess)
            {
                successCount++;
                continue;
            }

            if (attemptCount >= reg.MaxRetries)
            {
                exhaustedCount++;
                _logger.LogWarning(
                    "Handler {HandlerName} exhausted {Max} attempts for message {MessageId}",
                    reg.HandlerName, reg.MaxRetries, message.Id);
                continue;
            }

            var attemptNumber = attemptCount + 1;
            var stopwatch = Stopwatch.StartNew();
            InboxHandlerStatus status;
            string? errorMessage = null;

            try
            {
                var handler = (IInboxHandler)sp.GetRequiredService(reg.HandlerType);
                await handler.HandleAsync(message, ct);
                stopwatch.Stop();
                status = InboxHandlerStatus.Success;
                successCount++;

                InboxMetrics.HandlerSuccesses.Add(1,
                    new KeyValuePair<string, object?>("handler", reg.HandlerName),
                    new KeyValuePair<string, object?>("provider", message.ProviderKey),
                    new KeyValuePair<string, object?>("event_type", message.EventType));
            }
            catch (OperationCanceledException)
            {
                // Propagate cancellation — no attempt record, no retry scheduling.
                throw;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                errorMessage = ex.Message;
                failedCount++;
                lastError = ex.Message;

                // Reached the per-handler limit with this failure → will be treated as exhausted
                // on the next dispatch (attemptCount == MaxRetries at that point).
                status = attemptNumber >= reg.MaxRetries
                    ? InboxHandlerStatus.DeadLettered
                    : InboxHandlerStatus.Failed;

                InboxMetrics.HandlerFailures.Add(1,
                    new KeyValuePair<string, object?>("handler", reg.HandlerName),
                    new KeyValuePair<string, object?>("provider", message.ProviderKey),
                    new KeyValuePair<string, object?>("event_type", message.EventType));

                _logger.LogError(ex,
                    "Handler {HandlerName} failed for message {MessageId} (attempt {Attempt}/{Max})",
                    reg.HandlerName, message.Id, attemptNumber, reg.MaxRetries);
            }

            InboxMetrics.HandlerAttempts.Add(1,
                new KeyValuePair<string, object?>("handler", reg.HandlerName),
                new KeyValuePair<string, object?>("provider", message.ProviderKey),
                new KeyValuePair<string, object?>("event_type", message.EventType));
            InboxMetrics.HandlerDuration.Record(stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("handler", reg.HandlerName));

            newAttempts.Add(new InboxHandlerAttempt
            {
                Id = Guid.NewGuid(),
                InboxMessageId = message.Id,
                HandlerName = reg.HandlerName,
                AttemptNumber = attemptNumber,
                Status = status,
                DurationMs = (long)stopwatch.Elapsed.TotalMilliseconds,
                ErrorMessage = errorMessage,
                AttemptedAt = DateTimeOffset.UtcNow
            });

            // Sequential dispatch — stop on first failure so downstream handlers observe
            // a consistent entity state on the next retry.
            if (status != InboxHandlerStatus.Success)
                break;
        }

        return new DispatchOutcome(successCount, failedCount, exhaustedCount, lastError, newAttempts);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<bool> TrySaveAttemptsAsync(
        IInboxHandlerAttemptStore store,
        IReadOnlyList<InboxHandlerAttempt> attempts,
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
            _logger.LogError(ex, "Failed to save {Count} handler attempt(s)", attempts.Count);
            return false;
        }
    }

    // ── Bookkeeper ────────────────────────────────────────────────────────────

    /// <summary>
    /// Thread-safe staging buffer for the outcomes of a single dispatch batch. Per-message
    /// dispatch publishes into this; the flush stage drains it.
    /// </summary>
    private sealed class BatchBookkeeper
    {
        public ConcurrentBag<InboxHandlerAttempt> Attempts { get; } = new();

        // Ordered records preserved for the per-message flush path; the bulk path only needs
        // the IDs and the metric tag tuples.
        private readonly ConcurrentQueue<(Guid Id, string Provider, string EventType)> _successes = new();
        private readonly HashSet<Guid> _suppressed = new();
        private readonly object _suppressLock = new();

        public ConcurrentBag<(InboxMessage Message, string Error)> Failures { get; } = new();
        public ConcurrentBag<InboxMessage> DeadLetters { get; } = new();

        public void AddSuccess(InboxMessage message) =>
            _successes.Enqueue((message.Id, message.ProviderKey, message.EventType));

        public void AddFailure(InboxMessage message, string error) =>
            Failures.Add((message, error));

        public void AddDeadLetter(InboxMessage message) =>
            DeadLetters.Add(message);

        /// <summary>
        /// Marks a previously-recorded success as not-to-be-flushed. Used when an attempt
        /// save failed for that message and we'd rather retry-via-lock-expiry than risk
        /// double-dispatch.
        /// </summary>
        public void SuppressSuccess(Guid messageId)
        {
            lock (_suppressLock) _suppressed.Add(messageId);
        }

        public IReadOnlyCollection<Guid> SuccessIds
        {
            get
            {
                lock (_suppressLock)
                {
                    var ids = new List<Guid>(_successes.Count);
                    foreach (var rec in _successes)
                    {
                        if (!_suppressed.Contains(rec.Id)) ids.Add(rec.Id);
                    }
                    return ids;
                }
            }
        }

        public IEnumerable<(string Provider, string EventType)> SuccessTags
        {
            get
            {
                lock (_suppressLock)
                {
                    var tags = new List<(string, string)>(_successes.Count);
                    foreach (var rec in _successes)
                    {
                        if (!_suppressed.Contains(rec.Id)) tags.Add((rec.Provider, rec.EventType));
                    }
                    return tags;
                }
            }
        }

        public IEnumerable<(Guid Id, string Provider, string EventType)> EnumerateSuccessRecords()
        {
            lock (_suppressLock)
            {
                var list = new List<(Guid, string, string)>(_successes.Count);
                foreach (var rec in _successes)
                {
                    if (!_suppressed.Contains(rec.Id)) list.Add(rec);
                }
                return list;
            }
        }
    }
}
