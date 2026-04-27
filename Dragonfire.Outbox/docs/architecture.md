# Dragonfire.Outbox — Software Architecture Document

## 1. Why Dragonfire.Outbox Exists

### The Fundamental Problem: Reliable Cross-System Communication

Every non-trivial application eventually needs to do two things at once: **save data** and **tell someone about it**. An e-commerce system saves an order, then notifies the payment service. A SaaS platform creates a user, then fires a webhook to the tenant's endpoint. A logistics system marks a shipment dispatched, then alerts the tracking service.

These two operations — the database write and the network call — live in different failure domains. The database is local, fast, and transactional. The network call is remote, unpredictable, and can fail in ways that leave the caller unsure whether the receiver actually got the message.

This is called the **dual-write problem**, and it is the single most common source of data inconsistency in distributed systems.

### What Goes Wrong Without a Solution

Consider a simple order placement:

```
SaveOrder()          →  succeeds
SendPaymentWebhook() →  ???
```

Every possible failure mode produces a different kind of damage:

| Failure | Outcome | Business impact |
|---------|---------|-----------------|
| App crashes after DB write, before webhook | Order exists, payment never initiated | Revenue loss, stuck order |
| Webhook times out after 30s | Did receiver get it? Unknown | Potential double-charge or no charge |
| DB write succeeds, webhook returns 500 | Order exists, payment service rejected | Inconsistent state across services |
| App sends webhook, DB commit fails | Payment initiated for non-existent order | Phantom transaction, refund needed |
| Two webhooks needed (payment + inventory), one fails | One downstream updated, one not | Partial state — inventory reserved but no payment, or payment but no inventory |

These are not theoretical. In production systems processing hundreds of requests per second, network partitions, timeouts, and crashes happen **daily**. A fire-and-forget HTTP call after a database write is not a design choice — it is a bug waiting for production traffic to expose it.

### Why Naive Fixes Fail

**"Just wrap it in a try/catch and retry."** Retries introduce duplicate delivery. The receiver may have processed the first attempt but the response was lost. Now the payment is charged twice.

**"Use a distributed transaction."** Two-phase commit (2PC) requires both the database and the receiver to support the XA protocol. HTTP webhooks don't. Even when both sides support it, 2PC couples the availability of unrelated services — if the payment system is down, orders can't be placed.

**"Send the webhook first, then save to DB."** If the DB write fails, the receiver has a payment for an order that doesn't exist. This is worse than the original problem.

**"Use a message broker."** If the app writes to the database and then publishes to RabbitMQ/Kafka, you still have the dual-write problem — just between the database and the broker instead of between the database and the webhook. The broker doesn't participate in the DB transaction.

### The Correct Solution: Transactional Outbox Pattern

The only way to atomically couple a database write with a notification is to make the notification **part of the database write**. Instead of sending a webhook after the commit, you write a message to an outbox table **inside the same transaction**. A separate background process picks up outbox messages and delivers them.

```
BEGIN TRANSACTION
    INSERT INTO Orders (...)
    INSERT INTO OutboxMessages (EventType='order.placed', Payload='{...}')
COMMIT
```

If the transaction commits, the message is guaranteed to exist. If it rolls back, the message disappears with the order. There is no window where one exists without the other.

A background processor then reads the outbox, delivers webhooks, records delivery attempts, retries failures, and dead-letters messages that exhaust retries.

**Dragonfire.Outbox implements this pattern as a modular .NET library**, providing the transactional publish, background processing, webhook delivery, retry policies, observability, and persistence — so application developers don't have to build, test, and maintain this critical infrastructure themselves.

---

## 2. Architecture Overview

### System Context

```
┌──────────────────────────────────────────────────────────────────────────────┐
│                              Host Application                                │
│                                                                              │
│  ┌────────────────┐         ┌───────────────────┐        ┌──────────────┐   │
│  │  Domain Logic   │────────▶│  IOutboxPublisher  │───────▶│  SQL Server   │   │
│  │                 │  same   │  (transactional)   │  same  │  ┌─────────┐│   │
│  │  Order.Place()  │  tx     │                    │  tx    │  │ Orders  ││   │
│  │  User.Create()  │         └───────────────────┘        │  │ Outbox  ││   │
│  │  Shipment.Ship()│                                       │  │ Subs    ││   │
│  └────────────────┘                                       │  │ Attempts││   │
│                                                            │  └─────────┘│   │
│  ┌──────────────────────────────────────────────────────┐ │              │   │
│  │              Background Processor                     │ │              │   │
│  │                                                       │ │              │   │
│  │  ┌────────────┐  ┌─────────────┐  ┌──────────────┐  │ │              │   │
│  │  │ Lock Batch  │─▶│  Deliver    │─▶│  Write Back   │  │ │              │   │
│  │  │ (IOutbox    │  │  Webhooks   │  │  (Processed/  │  │ │              │   │
│  │  │  Store)     │  │  (IWebhook  │  │   Retry/DLQ)  │  │ │              │   │
│  │  │             │  │  Deliverer) │  │              │  │ │              │   │
│  │  └────────────┘  └──────┬──────┘  └──────────────┘  │ │              │   │
│  └──────────────────────────┼────────────────────────────┘ │              │   │
│                              │                              └──────────────┘   │
└──────────────────────────────┼───────────────────────────────────────────────┘
                               │
                    ┌──────────▼──────────┐
                    │   External Systems   │
                    │                      │
                    │  Payment Service     │
                    │  Inventory Service   │
                    │  Tenant Webhooks     │
                    │  Analytics Pipeline  │
                    └─────────────────────┘
```

### Processing Modes

Dragonfire.Outbox supports two processing topologies:

**Direct Delivery** (default) — the background processor reads the outbox and delivers webhooks directly via HTTP. Simple, no additional infrastructure.

```
Outbox Table  ──▶  Processor  ──▶  HTTP Webhook
```

**Queue-Mediated** — the processor reads the outbox and publishes to Azure Storage Queue. A separate consumer (or Azure Function) dequeues and delivers. Decouples polling from delivery.

```
Outbox Table  ──▶  Processor  ──▶  Azure Queue  ──▶  Consumer  ──▶  HTTP Webhook
```

---

## 3. Package Architecture

Dragonfire.Outbox is composed of seven NuGet packages arranged in a dependency tree that allows consumers to pull in only what they need.

```
                        Dragonfire.Outbox.Core
                       (contracts, models,
                        options, observability)
                      ┌────────┼────────────┬──────────────┐
                      │        │            │              │
                      ▼        ▼            ▼              ▼
          Dragonfire.Outbox.         Dragonfire.Outbox.   Dragonfire.Outbox.    Dragonfire.Outbox.
          Entity             SqlServer   Processor     Delivery
          FrameworkCore      (ADO.NET)   (background   (HTTP + HMAC
          (EF Core +                      service)      + retry)
           SQL Server)                       │
                                             │
                           ┌─────────────────┼──────────────┐
                           │                                │
                           ▼                                ▼
                    Dragonfire.Outbox.                        Dragonfire.Outbox.
                    AzureStorageQueue                 AzureFunctions
                    (queue transport)                 (serverless trigger)
```

### Package Responsibilities

| Package | Responsibility | Key Types |
|---------|---------------|-----------|
| **Dragonfire.Outbox.Core** | Defines all contracts, models, options, serialization, observability metrics, and the DI builder. Every other package depends on it. Contains zero implementation of persistence or delivery. | `IOutboxStore`, `IOutboxPublisher`, `ISubscriptionStore`, `IDeliveryAttemptStore`, `IWebhookDeliverer`, `IRetryPolicy`, `IOutboxProcessor`, `OutboxMessage`, `WebhookSubscription`, `DeliveryAttempt`, `OutboxOptions`, `OutboxMetrics`, `OutboxActivitySource` |
| **Dragonfire.Outbox.EntityFrameworkCore** | EF Core + SQL Server persistence. Provides `OutboxDbContext`, EF type configurations, EF-based stores, and a publisher that enlists in the caller's `DbContext` transaction. Zero raw SQL — all queries use LINQ and `ExecuteUpdateAsync`. | `OutboxDbContext`, `EfCoreOutboxStore`, `EfCoreSubscriptionStore`, `EfCoreDeliveryAttemptStore`, `EfCoreOutboxPublisher<TDbContext>`, `ModelBuilderExtensions` |
| **Dragonfire.Outbox.SqlServer** | Direct ADO.NET SQL Server persistence. No EF Core dependency. Each store opens its own `SqlConnection` from a connection string. The lock query uses `UPDATE...OUTPUT` with `UPDLOCK, READPAST` hints for maximum concurrent throughput. Publisher uses `ISqlTransactionAccessor` to join the caller's transaction. | `DirectSqlOutboxStore`, `DirectSqlSubscriptionStore`, `DirectSqlDeliveryAttemptStore`, `DirectSqlOutboxPublisher`, `ISqlTransactionAccessor` |
| **Dragonfire.Outbox.Processor** | Background processing engine. A `BackgroundService` that polls the outbox on a configurable interval, locks a batch, delivers via the configured mode, and handles retries/dead-lettering. Verifies lock ownership before every delivery to prevent duplicate processing across instances. | `OutboxProcessorService`, `OutboxProcessingPipeline` |
| **Dragonfire.Outbox.Delivery** | HTTP webhook delivery with HMAC-SHA256 signing, per-subscription timeouts, custom headers, and an exponential backoff retry policy with jitter. | `HttpWebhookDeliverer`, `HmacSignatureGenerator`, `ExponentialBackoffRetryPolicy` |
| **Dragonfire.Outbox.AzureStorageQueue** | Azure Storage Queue transport for the queue-mediated processing mode. Wraps messages in a JSON envelope and publishes to a configurable queue. | `AzureStorageQueuePublisher` |
| **Dragonfire.Outbox.AzureFunctions** | Azure Functions timer trigger that invokes the processing pipeline on a cron schedule (`*/10 * * * * *` by default). Enables serverless outbox processing without a long-running host. | `OutboxTimerFunction` |

---

## 4. Data Model

### Tables

All tables live in a configurable SQL schema (default: `outbox`).

#### `OutboxMessages`

The core queue. Messages are inserted transactionally, locked by the processor, and move through a state machine until delivered or dead-lettered.

| Column | Type | Purpose |
|--------|------|---------|
| `Id` | `uniqueidentifier` | PK, default `NEWSEQUENTIALID()` |
| `EventType` | `nvarchar(256)` | Routing key (e.g., `order.placed`) |
| `Payload` | `nvarchar(max)` | Serialized event body (JSON) |
| `CorrelationId` | `nvarchar(128)` | Caller-provided correlation for tracing |
| `TraceId` | `nvarchar(128)` | OpenTelemetry trace propagation |
| `Status` | `int` | Current state (see state machine below) |
| `RetryCount` | `int` | Number of failed delivery attempts |
| `CreatedAt` | `datetimeoffset(3)` | Insertion time |
| `ProcessedAt` | `datetimeoffset(3)` | Time of successful delivery |
| `LockedUntil` | `datetimeoffset(3)` | Visibility timeout expiry |
| `LockedBy` | `nvarchar(256)` | Processor instance identity |
| `NextRetryAt` | `datetimeoffset(3)` | Earliest time this message can be retried |
| `LastError` | `nvarchar(max)` | Most recent failure reason |
| `Headers` | `nvarchar(max)` | JSON dictionary of custom headers |

**Indexes:**
- `IX_OutboxMessages_Status_NextRetryAt` — batch lock query
- `IX_OutboxMessages_CreatedAt` — ordering
- `IX_OutboxMessages_EventType` — subscription routing

#### `WebhookSubscriptions`

Registered webhook endpoints. Each subscription targets a URL for a specific event type (or `*` for all events).

| Column | Type | Purpose |
|--------|------|---------|
| `Id` | `uniqueidentifier` | PK |
| `EventType` | `nvarchar(256)` | Event filter (`order.placed` or `*`) |
| `WebhookUrl` | `nvarchar(2048)` | Delivery endpoint |
| `Secret` | `nvarchar(512)` | HMAC-SHA256 signing key |
| `IsActive` | `bit` | Soft-delete flag |
| `MaxRetries` | `int` | Per-subscription retry limit |
| `TimeoutSeconds` | `int` | Per-subscription HTTP timeout |
| `CreatedAt` | `datetimeoffset(3)` | Creation time |
| `UpdatedAt` | `datetimeoffset(3)` | Last modification time |
| `CustomHeaders` | `nvarchar(max)` | JSON dictionary of extra HTTP headers |

#### `DeliveryAttempts`

Audit log. Every webhook delivery — successful or failed — is recorded with full HTTP details.

| Column | Type | Purpose |
|--------|------|---------|
| `Id` | `uniqueidentifier` | PK |
| `OutboxMessageId` | `uniqueidentifier` | FK → OutboxMessages |
| `WebhookSubscriptionId` | `uniqueidentifier` | FK → WebhookSubscriptions |
| `AttemptNumber` | `int` | Sequential attempt counter |
| `Status` | `int` | Pending / Success / Failed / DeadLettered |
| `HttpStatusCode` | `int?` | Response status (null if network error) |
| `ResponseBody` | `nvarchar(4000)` | Truncated response for debugging |
| `ErrorMessage` | `nvarchar(max)` | Exception message or failure detail |
| `DurationMs` | `bigint` | Round-trip time in milliseconds |
| `AttemptedAt` | `datetimeoffset(3)` | When the attempt was made |
| `NextRetryAt` | `datetimeoffset(3)` | Scheduled next attempt (if retrying) |

### Message State Machine

```
                           ┌──────────────────────────────────────┐
                           │                                      │
                           ▼                                      │
  ┌──────────┐      ┌─────────────┐      ┌─────────────┐        │
  │  Pending  │─────▶│  Processing  │─────▶│  Delivered   │        │
  │  (0)      │      │  (1)        │      │  (3)         │        │
  └──────────┘      └──────┬──────┘      └──────────────┘        │
       ▲                    │                                      │
       │                    │  delivery failed,                    │
       │                    │  retries remaining                   │
       │                    ▼                                      │
       │              set NextRetryAt                              │
       │              reset to Pending ────────────────────────────┘
       │
       │                    │  delivery failed,
       │                    │  retries exhausted
       │                    ▼
       │             ┌──────────────┐
       │             │ DeadLettered  │
       │             │ (5)           │
       │             └──────────────┘
       │
       │              lock expired
       │              (visibility timeout)
       └──────────── reset by ReleaseExpiredLocksAsync()
```

Key transitions:
- **Pending → Processing**: `LockNextBatchAsync` sets `Status=Processing`, `LockedBy`, `LockedUntil`
- **Processing → Delivered**: All subscriptions delivered successfully
- **Processing → Pending** (retry): At least one subscription failed, retries remaining — sets `NextRetryAt`, increments `RetryCount`, clears lock
- **Processing → DeadLettered**: Retries exhausted, message moved to dead letter for manual investigation
- **Processing → Pending** (lock expired): `ReleaseExpiredLocksAsync` reclaims messages whose `LockedUntil` has passed — safety net against crashed processors

---

## 5. Concurrency and Multi-Instance Safety

Dragonfire.Outbox is designed to run across multiple processor instances without duplicate delivery or lost messages. This section documents the locking protocol and the guarantees it provides.

### The Locking Protocol

Each message has three lock-related columns: `LockedBy`, `LockedUntil`, and `Status`.

**Lock acquisition** (`LockNextBatchAsync`):
1. Select messages where `Status IN (Pending, Processing)` AND `(LockedUntil IS NULL OR LockedUntil < NOW)` AND `(NextRetryAt IS NULL OR NextRetryAt <= NOW)`
2. Update matching rows: set `Status = Processing`, `LockedBy = instanceId`, `LockedUntil = NOW + visibilityTimeout`
3. Return the updated messages

**Lock ownership check** (`IsLockHeldAsync`):
Before delivering each message, the processor verifies `LockedBy == instanceId AND Status == Processing AND LockedUntil > NOW`.

**Lock-guarded write-back**:
Every state transition (`MarkAsProcessedAsync`, `IncrementRetryAsync`, `MarkAsDeadLetteredAsync`, `MarkAsFailedAsync`) includes `WHERE LockedBy = @lockedBy`. If another instance has stolen the lock, the UPDATE affects 0 rows and returns `false`.

**Lock expiry** (`ReleaseExpiredLocksAsync`):
At the start of each processing cycle, messages with `LockedUntil < NOW` are reset to `Pending`. This reclaims messages from crashed or stuck processors.

### Instance Identity

Each processor instance generates a globally unique identity at startup:

```csharp
InstanceId = $"{Environment.MachineName}-{Guid.NewGuid():N}"
```

This ensures that even in containerized environments where multiple replicas share a hostname, no two instances share an identity.

### Concurrent Processor Scenarios

#### EF Core store (no SQL lock hints)

The EF Core `LockNextBatchAsync` uses `ExecuteUpdateAsync` with a `WHERE LockedUntil < NOW` filter. Under SQL Server's READ COMMITTED isolation:

- Two processors issuing concurrent `ExecuteUpdateAsync` calls: one acquires row-level X locks first. The second blocks on those locks.
- When the first commits, the second re-evaluates the predicate. Since `LockedUntil` is now in the future, the rows are excluded.
- The second processor gets a different batch (or an empty one if no more messages are pending).

**Trade-off**: Under high concurrency, the second processor briefly blocks instead of instantly skipping locked rows. For most workloads (single instance or polling intervals of several seconds), this is negligible.

#### Direct SQL store (UPDLOCK, READPAST hints)

The direct SQL `LockNextBatchAsync` uses `UPDATE...OUTPUT...WITH (UPDLOCK, READPAST)`:

- `READPAST` instructs SQL Server to skip rows that are locked by other transactions, instead of blocking.
- `UPDLOCK` acquires update locks during the scan phase, preventing two processors from selecting the same rows.
- The `OUTPUT INSERTED.*` clause returns the updated rows atomically in a single round-trip.

**Result**: Multiple processor instances can poll concurrently without blocking each other. Each gets a distinct batch. This is optimal for high-throughput, multi-instance deployments.

### Stolen Lock Protection

If a processor's delivery takes longer than the visibility timeout:

1. `ReleaseExpiredLocksAsync` (called by another instance) resets the message to `Pending`
2. Another instance locks and begins processing the same message
3. The original processor finishes delivery and calls `MarkAsProcessedAsync`
4. The `WHERE LockedBy = @lockedBy` guard fails (returns `false`) — the write-back is silently rejected
5. The new owner completes delivery independently

**Worst case**: The webhook is delivered twice (once by the original, once by the new owner). This is inherent in at-least-once delivery systems. Receivers must be idempotent — Dragonfire.Outbox provides `X-Outbox-Delivery-Id` headers to support deduplication.

---

## 6. Webhook Delivery

### Request Format

Every webhook delivery sends an HTTP POST with the following headers:

| Header | Purpose |
|--------|---------|
| `Content-Type: application/json` | Payload encoding |
| `X-Outbox-Signature` | HMAC-SHA256 signature: `sha256=<hex>` |
| `X-Outbox-Event` | Event type (e.g., `order.placed`) |
| `X-Outbox-Delivery-Id` | Unique ID for this delivery attempt (for deduplication) |
| `X-Outbox-Timestamp` | Unix timestamp of the delivery attempt |
| `X-Outbox-Correlation-Id` | Correlation ID (if provided by the publisher) |
| *Custom headers* | Per-subscription custom headers from `WebhookSubscription.CustomHeaders` |

### Signature Verification

The `X-Outbox-Signature` header contains an HMAC-SHA256 hash of the request body, computed with the subscription's shared secret:

```
HMAC-SHA256(key=subscription.Secret, data=message.Payload)
→ sha256=<lowercase hex>
```

Receivers should verify this signature to ensure the webhook is authentic and hasn't been tampered with.

### Retry Policy

Failed deliveries are retried with exponential backoff and jitter:

```
delay = min(baseDelay × 2^retryCount, maxDelay) ± jitter
```

| Parameter | Default | Description |
|-----------|---------|-------------|
| `MaxRetries` | 5 | Maximum number of retry attempts |
| `BaseDelay` | 5 seconds | Starting delay |
| `MaxDelay` | 5 minutes | Upper bound on delay |
| `JitterFactor` | 0.2 (±20%) | Randomization to prevent thundering herd |

After `MaxRetries` attempts, the message is moved to the `DeadLettered` state for manual investigation.

### Per-Subscription Configuration

Each `WebhookSubscription` has independent settings:

- **Timeout**: HTTP request timeout (default 30s). Slow endpoints don't affect faster ones.
- **MaxRetries**: Override the global retry count per subscription.
- **Secret**: Unique HMAC key per endpoint.
- **CustomHeaders**: Additional HTTP headers (e.g., `Authorization`, `X-Tenant-Id`).

---

## 7. Observability

### Distributed Tracing (OpenTelemetry)

Dragonfire.Outbox creates `Activity` spans via `System.Diagnostics.ActivitySource` named `"Dragonfire.Outbox"`:

| Span | Tags | Created by |
|------|------|-----------|
| `outbox.publish` | `outbox.event_type`, `outbox.message_id` | `IOutboxPublisher.PublishAsync` |
| `outbox.process_batch` | `outbox.batch_size` | `OutboxProcessingPipeline.ProcessBatchAsync` |
| `outbox.deliver_webhook` | `outbox.message_id`, `outbox.subscription_id`, `outbox.event_type`, `outbox.webhook_url`, `http.status_code`, `outbox.delivery.success` | `HttpWebhookDeliverer.DeliverAsync` |

To collect these spans, register the source in your OpenTelemetry configuration:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource("Dragonfire.Outbox"));
```

### Metrics (System.Diagnostics.Metrics)

Dragonfire.Outbox exposes a `Meter` named `"Dragonfire.Outbox"` with the following instruments:

| Metric | Type | Description |
|--------|------|-------------|
| `outbox.messages.published` | Counter | Messages written to the outbox |
| `outbox.messages.processed` | Counter | Messages successfully delivered |
| `outbox.messages.failed` | Counter | Messages that failed (will retry) |
| `outbox.messages.dead_lettered` | Counter | Messages moved to dead letter |
| `outbox.delivery.attempts` | Counter | Total webhook HTTP attempts |
| `outbox.delivery.successes` | Counter | Successful HTTP deliveries |
| `outbox.delivery.failures` | Counter | Failed HTTP deliveries |
| `outbox.delivery.duration_ms` | Histogram | Webhook round-trip time |
| `outbox.processing.duration_ms` | Histogram | Batch processing time |
| `outbox.batches.processed` | Counter | Number of batches processed |
| `outbox.batch.size` | Histogram | Messages per batch |

All counters are tagged with `event_type` for per-event-type breakdowns.

---

## 8. Configuration Reference

### OutboxOptions

```csharp
services.AddOutboxNet(options =>
{
    options.SchemaName = "outbox";                            // SQL schema name
    options.BatchSize = 50;                                   // Messages per processing cycle
    options.DefaultVisibilityTimeout = TimeSpan.FromSeconds(60); // Lock duration
    options.InstanceId = "custom-id";                         // Override auto-generated instance ID
    options.MaxConcurrentDeliveries = 10;                     // Parallel webhook calls per batch
    options.ProcessingMode = ProcessingMode.DirectDelivery;   // or QueueMediated
});
```

### ProcessorOptions

```csharp
.AddBackgroundProcessor(options =>
{
    options.PollingInterval = TimeSpan.FromSeconds(10);       // How often to poll for new messages
});
```

### WebhookDeliveryOptions

```csharp
.AddWebhookDelivery(options =>
{
    options.HttpTimeout = TimeSpan.FromSeconds(30);           // Default HTTP timeout
    options.Retry.MaxRetries = 5;
    options.Retry.BaseDelay = TimeSpan.FromSeconds(5);
    options.Retry.MaxDelay = TimeSpan.FromMinutes(5);
    options.Retry.JitterFactor = 0.2;
});
```

---

## 9. Integration Patterns

### Pattern 1: EF Core Application with Background Processor

The most common setup. The outbox publisher enlists in your application's EF Core transaction.

```csharp
// Program.cs
builder.Services.AddOutboxNet(o => o.SchemaName = "outbox")
    .UseSqlServer<AppDbContext>(connectionString)
    .AddBackgroundProcessor(o => o.PollingInterval = TimeSpan.FromSeconds(5))
    .AddWebhookDelivery();

// Command handler
await using var tx = await _db.Database.BeginTransactionAsync(ct);
_db.Orders.Add(order);
await _db.SaveChangesAsync(ct);
await _outbox.PublishAsync("order.placed", new { order.Id, order.Total }, ct: ct);
await tx.CommitAsync(ct);
```

### Pattern 2: Dapper/ADO.NET Application

For applications that don't use EF Core. The consumer provides `ISqlTransactionAccessor` to let the publisher join their transaction.

```csharp
// Program.cs
builder.Services.AddOutboxNet()
    .UseDirectSqlServer(connectionString)
    .AddBackgroundProcessor()
    .AddWebhookDelivery();
builder.Services.AddScoped<ISqlTransactionAccessor, MySqlTransactionAccessor>();

// Command handler
await using var conn = new SqlConnection(connectionString);
await conn.OpenAsync(ct);
await using var tx = conn.BeginTransaction();
// ... domain writes with Dapper/ADO.NET ...
_txAccessor.Connection = conn;
_txAccessor.Transaction = tx;
await _outbox.PublishAsync("order.placed", payload, ct: ct);
await tx.CommitAsync(ct);
```

### Pattern 3: Serverless with Azure Functions

The outbox is processed by an Azure Functions timer trigger instead of a background service. No long-running host required.

```csharp
// Program.cs (Azure Functions isolated worker)
builder.Services.AddOutboxNet()
    .UseSqlServer<AppDbContext>(connectionString)  // or UseDirectSqlServer
    .AddAzureFunctionsProcessor()
    .AddWebhookDelivery();
```

### Pattern 4: Queue-Mediated Processing

For high-throughput systems where you want to decouple polling from delivery. The processor pushes to Azure Storage Queue; delivery happens separately.

```csharp
builder.Services.AddOutboxNet(o => o.ProcessingMode = ProcessingMode.QueueMediated)
    .UseSqlServer<AppDbContext>(connectionString)
    .UseAzureStorageQueue(o =>
    {
        o.ConnectionString = azureStorageConnString;
        o.QueueName = "outbox-messages";
    })
    .AddBackgroundProcessor();
```

---

## 10. Value Proposition Summary

| Without Dragonfire.Outbox | With Dragonfire.Outbox |
|---|---|
| Database writes and webhook calls are separate operations that can partially fail | Outbox message is written in the same DB transaction — atomic by construction |
| App crash between DB commit and webhook → lost notification | Message persists in the outbox regardless of app crashes — processor delivers it |
| No visibility into whether webhooks were delivered | Full audit trail in `DeliveryAttempts` with HTTP status, response, duration |
| Manual retry logic per integration point | Configurable exponential backoff with jitter, dead-letter queue, per-subscription settings |
| Webhook receivers can't verify authenticity | HMAC-SHA256 signed payloads with per-subscription secrets |
| Multiple processor instances can duplicate delivery | Lock ownership protocol with instance identity, visibility timeouts, and guarded write-backs |
| No standardized observability | Built-in OpenTelemetry traces and System.Diagnostics.Metrics for dashboards and alerting |
| Each team builds ad-hoc outbox implementations | Single, tested library with modular packages — use only what you need |
