# InboxNet — Architecture

## 1. What InboxNet Solves

Receiving webhooks reliably is harder than it looks. The naive implementation — validate the signature, do business logic, return 200 — has three failure modes that appear only under production traffic:

| Failure | Consequence |
|---|---|
| Handler throws after you return 200 | Event lost permanently — provider will not retry |
| Handler throws before you return 200 | Provider retries, handler runs twice with no dedup |
| Process restarts mid-handler | No record of what happened; event may be lost or re-processed |

InboxNet eliminates these by separating *receiving* from *processing*:

1. **Receive**: validate signature, persist to `InboxMessages` (with dedup), return 202
2. **Process**: background dispatcher locks messages, runs handlers, retries failures, dead-letters exhausted messages

The inbox table is in the same SQL Server database as your domain data. This means the acknowledgement (returning 202) and the durability guarantee (the row in the DB) are the same atomic operation. A crash before the INSERT never returns 202. A crash after the INSERT leaves a row the dispatcher will pick up.

---

## 2. Request Flow

```
External Provider
      │
      │  POST /webhooks/{providerKey}
      ▼
┌─────────────────────────────────────────┐
│  MapInboxWebhooks (ASP.NET Core endpoint)│
│                                          │
│  1. Buffer raw body (once, for HMAC)     │
│  2. Resolve IWebhookProvider by key      │──── key unknown ────▶ 404
│  3. provider.ParseAsync()                │──── invalid sig ────▶ 400
│  4. IInboxPublisher.PublishAsync()       │
│     ├─ INSERT OR GET DUPLICATE           │──── duplicate ──────▶ 200 OK
│     └─ Signal hot-path channel           │
│  5. Return 202 Accepted                  │
└──────────────────────────────────────────┘
              │
      ┌───────▼──────────┐
      │  InboxMessages    │
      │  Status = Pending │
      └───────┬───────────┘
              │
┌─────────────▼──────────────────────────────────┐
│             InboxProcessorService               │
│  (BackgroundService, one per host instance)     │
│                                                 │
│  HOT PATH  Task 1                               │
│  ──────────────────────────────────────────     │
│  Parallel.ForEachAsync(channel.ReadAllAsync())  │
│    ├─ add ID to _hotInFlight set                │
│    ├─ TryLockByIdAsync (PK-seek UPDATE)         │
│    │    WHERE Id=@id AND Status=Pending         │
│    │    AND LockedUntil < NOW                   │
│    ├─ if locked: DispatchMessageAsync()         │
│    └─ remove from _hotInFlight                  │
│                                                 │
│  COLD PATH  Task 2                              │
│  ──────────────────────────────────────────     │
│  loop every ColdPollingInterval (default: 1s)   │
│    ├─ snapshot _hotInFlight → skipIds JSON      │
│    ├─ LockNextBatchAsync                        │
│    │    WHERE Id NOT IN (skipIds via OPENJSON)  │
│    │    AND Status=Pending                      │
│    │    AND NextRetryAt <= NOW                  │
│    └─ for each message: DispatchMessageAsync()  │
└────────────────────────────────────────────────┘
              │
┌─────────────▼──────────────────────────────────┐
│             DispatchMessageAsync                │
│                                                 │
│  1. GetMatching(providerKey, eventType)         │
│     ─ returns ordered list of handler regs      │
│                                                 │
│  2. GetHandlerStatesAsync(messageId)            │
│     ─ per-handler (AttemptCount, HasSuccess)    │
│                                                 │
│  3. foreach handler in order:                   │
│     ├─ skip if HasSuccess (already succeeded)   │
│     ├─ skip if AttemptCount >= MaxRetries        │
│     ├─ handler.HandleAsync(message)             │
│     └─ stop chain on first failure              │
│                                                 │
│  4. SaveAttemptsAsync([new attempt records])    │
│                                                 │
│  5. Decision:                                   │
│     ├─ all ok          → MarkAsProcessed        │
│     ├─ any failed      → IncrementRetry         │
│     └─ all exhausted   → MarkAsDeadLettered     │
└────────────────────────────────────────────────┘
```

---

## 3. Package Architecture

```
                     InboxNet.Core
                  (contracts, models,
                   options, observability)
                 ┌────────┬─────────────┬────────────┐
                 │        │             │            │
                 ▼        ▼             ▼            ▼
         InboxNet.   InboxNet.    InboxNet.    InboxNet.
         Entity      AspNetCore   Processor    Providers
         Framework   (endpoint    (dispatcher  (Stripe,
         Core        extension)   service)     GitHub,
         (EF Core                              HMAC)
          stores,
          publisher)
```

### Package responsibilities

| Package | Key types | Purpose |
|---|---|---|
| **InboxNet.Core** | `IInboxHandler`, `IWebhookProvider`, `IInboxStore`, `IInboxPublisher`, `IInboxHandlerRegistry`, `InboxMessage`, `InboxOptions`, `InboxMetrics` | All contracts and models. Zero persistence or HTTP. |
| **InboxNet.EntityFrameworkCore** | `InboxDbContext`, `EfCoreInboxStore`, `EfCoreInboxPublisher`, `ModelBuilderExtensions` | EF Core persistence. Provides `UseSqlServer()` on the builder. |
| **InboxNet.AspNetCore** | `InboxEndpointRouteBuilderExtensions` | `MapInboxWebhooks()` / `MapInboxWebhook()` minimal-API endpoints. |
| **InboxNet.Processor** | `InboxProcessorService`, `InboxProcessingPipeline` | `BackgroundService` running hot + cold dispatch loops. Provides `AddBackgroundDispatcher()`. |
| **InboxNet.Providers** | `StripeWebhookProvider`, `GitHubWebhookProvider`, `GenericHmacWebhookProvider` | Ready-made signature validators for common providers. Provides `AddStripeProvider()`, `AddGitHubProvider()`, `AddGenericHmacProvider()`. |

---

## 4. Data Model

All tables live in a configurable SQL schema (default: `inbox`).

### `InboxMessages`

The durable queue. Messages are inserted on receipt, locked for dispatch, and move through a state machine until processed or dead-lettered.

| Column | Type | Purpose |
|---|---|---|
| `Id` | `uniqueidentifier` | PK |
| `ProviderKey` | `nvarchar(128)` | Which provider delivered this (e.g. `"stripe"`) |
| `EventType` | `nvarchar(256)` | Provider's canonical event name (e.g. `"invoice.paid"`) |
| `Payload` | `nvarchar(max)` | Raw request body as received |
| `ContentSha256` | `nvarchar(64)` | SHA-256 of `Payload` — part of dedup key |
| `ProviderEventId` | `nvarchar(256)?` | Provider's own stable event ID when available |
| `DedupKey` | `nvarchar(256)` | `ProviderEventId` if set, else `ContentSha256` |
| `Status` | `int` | State machine value (see below) |
| `RetryCount` | `int` | Number of failed dispatch cycles |
| `ReceivedAt` | `datetimeoffset(3)` | When the HTTP request was received |
| `ProcessedAt` | `datetimeoffset(3)?` | When all handlers succeeded |
| `LockedUntil` | `datetimeoffset(3)?` | Visibility timeout expiry |
| `LockedBy` | `nvarchar(256)?` | Dispatcher instance identity |
| `NextRetryAt` | `datetimeoffset(3)?` | Earliest time this message can be retried |
| `LastError` | `nvarchar(max)?` | Most recent handler failure message |
| `CorrelationId` | `nvarchar(128)?` | From provider parse result |
| `TraceId` | `nvarchar(128)?` | OpenTelemetry trace propagation |
| `TenantId` | `nvarchar(256)?` | From provider parse result (e.g. Stripe Connect account) |
| `EntityId` | `nvarchar(256)?` | From provider parse result — drives ordered dispatch |

**Unique index:** `(ProviderKey, DedupKey)` — enforces deduplication at the database level.

### `InboxHandlerAttempts`

Per-handler attempt log. Enables skip-on-success retry semantics.

| Column | Type | Purpose |
|---|---|---|
| `Id` | `uniqueidentifier` | PK |
| `InboxMessageId` | `uniqueidentifier` | FK → InboxMessages |
| `HandlerName` | `nvarchar(512)` | Stable handler identity (type full name by default) |
| `AttemptNumber` | `int` | 1-based attempt counter per handler |
| `Status` | `int` | Success / Failed / DeadLettered |
| `DurationMs` | `bigint` | Handler execution time |
| `ErrorMessage` | `nvarchar(max)?` | Exception message on failure |
| `AttemptedAt` | `datetimeoffset(3)` | When this attempt ran |

### Message state machine

```
             ┌──────────┐
   receive   │  Pending  │
  ──────────▶│   (0)    │
             └────┬─────┘
                  │ lock
                  ▼
             ┌────────────┐
             │ Processing  │
             │   (1)      │
             └──┬──────┬──┘
                │      │
    all ok      │      │  failure,
                ▼      │  retries remain
          ┌──────────┐  │
          │Processed │  │ set NextRetryAt,
          │  (3)     │  │ increment RetryCount,
          └──────────┘  │ clear lock
                        ▼
                   ┌──────────┐
                   │ Pending  │◀── cold path picks up
                   │   (0)    │    after NextRetryAt
                   └────┬─────┘
                        │
                        │  retries exhausted
                        ▼
                  ┌────────────┐
                  │DeadLettered│
                  │   (5)      │
                  └────────────┘

  Any state with expired LockedUntil:
  ReleaseExpiredLocksAsync() → reset to Pending
```

---

## 5. Hot Path and Cold Path

### Hot path

On every `PublishAsync()` call, after the INSERT commits, the publisher writes the new message ID to a `Channel<Guid>` (capacity 10,000, `DropOldest`). The hot path drains the channel continuously with `Parallel.ForEachAsync`.

For each ID dequeued:

1. Add to `_hotInFlight` (`ConcurrentDictionary<Guid, byte>`)
2. `TryLockByIdAsync` — single-row `UPDATE WHERE Id=@id AND Status=Pending AND LockedUntil < NOW OUTPUT INSERTED.*`
3. If locked: run `DispatchMessageAsync`
4. If not locked (another instance beat us): do nothing
5. Remove from `_hotInFlight` in `finally`

Same-instance latency from publish to first handler invocation is typically under 1 ms.

### Cold path

Runs every `ColdPollingInterval` (default 1 second) regardless of channel activity. Before querying, it snapshots `_hotInFlight` and passes the IDs as `@skipJson`:

```sql
AND m.[Id] NOT IN (SELECT CAST([value] AS uniqueidentifier) FROM OPENJSON(@skipJson))
```

This prevents the cold path from racing against the hot path for IDs currently being processed on this instance. Cross-instance concurrency is handled by `WITH (UPDLOCK, READPAST)` on the batch lock query — if another instance holds an update lock on a row, this instance skips it rather than blocking.

The cold path covers four scenarios the hot path cannot:

| Scenario | Cause |
|---|---|
| Message from another instance | Hot path channel is in-process only |
| Scheduled retry | `NextRetryAt > NOW` when published; becomes eligible later |
| Channel overflow | Burst > 10,000 dropped the hint; row is still in the DB |
| Expired lock recovery | `ReleaseExpiredLocksAsync` (throttled to once per 30 s) resets stuck messages |

---

## 6. Per-Handler Attempt Tracking

Unlike a simple "retry the whole message" model, InboxNet tracks each handler independently.

Before dispatching, the pipeline calls `GetHandlerStatesAsync(messageId, handlerNames)`, which returns `(AttemptCount, HasSuccess)` per handler name. The dispatch loop then:

- **Skips** handlers where `HasSuccess = true` — they already did their work
- **Skips** handlers where `AttemptCount >= MaxRetries` — they are exhausted
- **Runs** all remaining handlers sequentially, stopping the chain on the first failure

This means if you have three handlers and the second one fails, retries run only handlers 2 and 3 (handler 1's success is recorded and skipped). Handler names are stable identifiers — changing a handler's registered name after deployment makes prior success records invisible to it.

**Bookkeeping failure safety:** if `SaveAttemptsAsync` throws after a handler has already succeeded, InboxNet does not schedule an immediate retry. Instead it leaves the lock in place to expire. When the lock expires, `ReleaseExpiredLocksAsync` resets the message to Pending, and the next dispatch reads whatever partial attempt records did save. Handlers that succeeded and whose records were saved will be skipped. This is why handlers must be idempotent.

---

## 7. Deduplication

Dedup operates at two levels:

**Database level (hard dedup):** `InboxMessages` has a unique index on `(ProviderKey, DedupKey)`. If a second INSERT arrives with the same pair, the store returns the existing row's ID with `IsDuplicate = true`. The endpoint returns `200 OK` — a safe acknowledgement that tells the provider "we already have this, stop retrying."

**Dedup key selection:**
- If the provider sets `ProviderEventId` in its `WebhookParseResult` (e.g. Stripe's `evt_...` or GitHub's `X-GitHub-Delivery`), that value is the dedup key. Stable across any number of provider retries.
- If the provider does not supply an event ID, the dedup key is SHA-256 of the raw body. This catches exact-duplicate retries but not re-deliveries with mutated bodies.

---

## 8. Multi-Instance Safety

Multiple instances share the same SQL Server database. Safety guarantees:

**Lock acquisition is atomic.** `TryLockByIdAsync` and `LockNextBatchAsync` both use `UPDATE ... OUTPUT INSERTED.*`. SQL Server evaluates the `WHERE` predicate and applies the lock atomically — only one instance can update a given row.

**Write-backs are lock-guarded.** Every terminal operation includes `WHERE LockedBy = @lockedBy`. If another instance reclaimed the lock (e.g. after lock expiry), the update affects 0 rows and is silently ignored.

**Instance identity is globally unique.** Default `InstanceId = "{MachineName}-{Guid.NewGuid():N}"` — unique even across containers sharing a hostname.

**Lock expiry.** If a dispatcher instance crashes mid-handler, `LockedUntil` expires and `ReleaseExpiredLocksAsync` resets the message to Pending. The retry does not increment `RetryCount` — an expired lock indicates infrastructure failure, not a handler failure. Counting it against the retry budget would dead-letter healthy messages after pod restarts.

**Worst case:** a handler runs twice (once by the crashed instance, once by the recovery). InboxNet provides at-least-once delivery. Handlers must be idempotent on `InboxMessage.Id`.

---

## 9. Provider Model

Providers do two things: validate the request's authenticity and extract canonical event fields. They are stateless adapters between an external provider's wire format and InboxNet's `WebhookParseResult`.

```csharp
public interface IWebhookProvider
{
    string Key { get; }   // stable routing key, e.g. "stripe"
    Task<WebhookParseResult> ParseAsync(WebhookRequestContext context, CancellationToken ct);
}
```

`WebhookRequestContext` carries the buffered raw body (buffered once, before any provider sees it), all request headers, the route path, and query string.

`WebhookParseResult.Valid(...)` carries:
- `EventType` — matched against handler registrations
- `Payload` — raw body, stored as-is in `InboxMessages.Payload`
- `ContentSha256` — for dedup when no provider event ID is available
- `ProviderEventId` — preferred dedup key
- `EntityId` — optional; drives ordered dispatch
- `TenantId` — optional; stored for tenant-scoped queries and dispatch filtering
- `CorrelationId` — forwarded to the message for tracing

For providers where signature validation and payload parsing are separate concerns (e.g. shared validator across multiple payload schemas), register them as a keyed pair:

```csharp
.AddProvider<MySignatureValidator, MyPayloadMapper>("my-provider")
```

The builder composes them into a `CompositeWebhookProvider` automatically.

---

## 10. Observability

InboxNet emits OpenTelemetry signals via `System.Diagnostics.ActivitySource` and `System.Diagnostics.Metrics`, both named `"InboxNet"`.

### Activity spans

| Span | When | Key tags |
|---|---|---|
| `inbox.receive` | Per HTTP request | `inbox.provider_key`, `inbox.valid` |
| `inbox.process_batch` | Per cold-path cycle | `inbox.batch_size` |
| `inbox.dispatch` | Per message | `inbox.message_id`, `inbox.provider_key`, `inbox.event_type` |

### Metrics

| Metric | Type | Tags |
|---|---|---|
| `inbox.messages.received` | Counter | `provider` |
| `inbox.messages.invalid` | Counter | `provider` |
| `inbox.messages.processed` | Counter | `provider`, `event_type` |
| `inbox.messages.failed` | Counter | `provider`, `event_type` |
| `inbox.messages.dead_lettered` | Counter | `provider`, `event_type` |
| `inbox.handlers.attempts` | Counter | `handler`, `provider`, `event_type` |
| `inbox.handlers.successes` | Counter | `handler`, `provider`, `event_type` |
| `inbox.handlers.failures` | Counter | `handler`, `provider`, `event_type` |
| `inbox.handlers.duration_ms` | Histogram | `handler` |
| `inbox.batches.processed` | Counter | — |
| `inbox.batch.size` | Histogram | — |
| `inbox.processing.duration_ms` | Histogram | — |
