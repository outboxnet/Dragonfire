# SagaNet

A .NET library for running **workflows** and **sagas** — reliable, step-by-step business processes that survive crashes, retries, and partial failures.

---

## Table of Contents

1. [What problem does this solve?](#1-what-problem-does-this-solve)
2. [Core concepts — plain English](#2-core-concepts--plain-english)
   - [Workflow](#workflow)
   - [Step](#step)
   - [Saga & Compensation](#saga--compensation)
   - [Execution Pointer](#execution-pointer)
3. [How SagaNet works internally](#3-how-saganet-works-internally)
4. [A full example — Order Workflow](#4-a-full-example--order-workflow)
   - [Happy path](#happy-path)
   - [What happens when a step fails?](#what-happens-when-a-step-fails)
5. [Getting started](#5-getting-started)
6. [Defining steps](#6-defining-steps)
7. [Defining a workflow](#7-defining-a-workflow)
8. [What should be a separate step?](#8-what-should-be-a-separate-step)
9. [Waiting for an external event](#9-waiting-for-an-external-event)
10. [Configuring retry behaviour](#10-configuring-retry-behaviour)
11. [Observability — logs, traces, metrics](#11-observability--logs-traces-metrics)
12. [Configuration reference](#12-configuration-reference)
13. [Project structure](#13-project-structure)

---

## 1. What problem does this solve?

Imagine you are writing code that places an order in an e-commerce system. The business process has several steps:

1. Validate the order
2. Reserve the items in the warehouse
3. Charge the customer's card
4. Ship the parcel

In a simple world you would call these one after another in a single function. But the real world is messy:

- **What if the app crashes mid-way?** The warehouse reserved stock but the card was never charged. The order is stuck in a broken state forever.
- **What if the payment gateway times out?** You need to retry — but how many times, and with what delay?
- **What if the charge succeeds but shipping fails?** You need to refund the customer and release the stock.

Handling all of this manually leads to brittle, tangled code. SagaNet gives you a clean, structured way to describe the process and handles the hard parts — retrying, persisting state, and rolling back — automatically.

---

## 2. Core concepts — plain English

### Workflow

A **workflow** is a recipe — a named, ordered list of steps that together accomplish one business goal.

Think of it like a checklist:

```
□ Validate order
□ Reserve inventory
□ Charge payment
□ Ship order
```

A workflow *definition* is the template (the blank checklist). A workflow *instance* is one specific run of it (a filled-in checklist for order #42).

SagaNet persists each instance in a SQL database, so even if your application restarts, the workflow picks up exactly where it left off.

---

### Step

A **step** is a single item on the checklist. You write a C# class for each step. The class has one job: do its work, then tell SagaNet what to do next.

A step returns one of these outcomes:

| Outcome | Meaning |
|---|---|
| `ExecutionResult.Next()` | I succeeded. Move to the next step. |
| `ExecutionResult.Complete()` | I succeeded and this is the last step. |
| `ExecutionResult.Retry(delay)` | Something went wrong temporarily. Try me again after this delay. |
| `ExecutionResult.Fail(reason)` | Something went permanently wrong. Trigger rollback. |
| `ExecutionResult.WaitForEvent(name, key)` | Pause here until an external event arrives (e.g. payment callback). |

---

### Saga & Compensation

A **saga** is a workflow where each step can declare an *undo action* — called **compensation**.

The classic analogy: imagine booking a holiday.

1. Book flight ✈️  
2. Book hotel 🏨  
3. Book car hire 🚗  

If step 3 (car hire) fails, you don't want to leave the customer with a flight and a hotel they can't use. You need to **undo** the hotel booking and the flight booking — in reverse order.

In SagaNet, you mark a step as *compensatable* by implementing `ICompensatableStep<T>`. If any later step fails, SagaNet automatically calls the `CompensateAsync` method of each previously-completed step, in reverse order.

```
Normal flow:      Book Flight → Book Hotel → Book Car  ← fails here
Compensation:                   ✗ Cancel Hotel ← ✗ Cancel Flight
```

You never write the "walk back" logic yourself — SagaNet drives it.

---

### Execution Pointer

Every step in a running workflow has an **execution pointer** — a small record that tracks:

- Which step this is
- Its current status (Pending / Running / Complete / Compensating / Failed / WaitingForEvent)
- How many times it has been attempted
- Any error message from the last failure
- When it last ran, and when it should run next (for retries)

Execution pointers are persisted alongside the workflow instance in the database. This is what allows SagaNet to resume correctly after a crash.

---

## 3. How SagaNet works internally

Here is what happens behind the scenes when you start a workflow:

```
Your code
  │
  ▼
IWorkflowHost.StartWorkflowAsync(...)
  │  Creates a WorkflowInstance record in SQL Server (Status = Runnable)
  │
  ▼
WorkflowHost (IHostedService — runs in the background)
  │  Polls the database every 500 ms for Runnable instances
  │  Uses a Channel<Guid> to dispatch work to N parallel workers
  │
  ▼
WorkflowExecutor.ExecuteAsync(instanceId)
  │  Tries to "acquire" the instance using optimistic concurrency (RowVersion)
  │  → If two app servers compete, only one wins; the other skips safely
  │
  ▼
TickAsync — advances the workflow by one step
  │  Finds the next Pending execution pointer
  │  Calls StepExecutor to run it
  │
  ▼
StepExecutor
  │  Resolves the step class from the DI container
  │  Runs it through the Middleware Pipeline:
  │    ObservabilityMiddleware  ← records logs, traces, metrics
  │    RetryMiddleware          ← catches exceptions, converts to Retry result
  │    Your step's RunAsync()
  │
  ▼
ApplyResult
  │  Next      → mark step Complete, queue the next step, save to DB
  │  Retry     → set RetryAfter timestamp, save to DB, pick up on next poll
  │  Fail      → trigger SagaCompensator (walks back completed steps)
  │  WaitEvent → set status Suspended, wake up when event arrives
  │  Complete  → mark workflow terminal
```

This entire cycle happens for **one step at a time**. The next step runs on the next poll cycle (or immediately if it is ready). This design means:

- The database is always the source of truth.
- Any number of app servers can run simultaneously without double-executing a step.
- A crash at any point is safe — the step will simply be retried on restart.

---

## 4. A full example — Order Workflow

### Happy path

```
[ValidateOrder] ──► [ReserveInventory] ──► [ProcessPayment] ──► [ShipOrder]
```

1. **ValidateOrder** — checks that the order has an ID and a positive amount. Returns `Next()`.
2. **ReserveInventory** — calls the warehouse service to hold the items. Returns `Next()`.
3. **ProcessPayment** — calls the payment gateway. Returns `Next()`.
4. **ShipOrder** — dispatches the parcel. Returns `Complete()`.

Each step saves its result (flags set on `OrderData`) and the data is serialized to the database. If the app restarts between steps 2 and 3, step 3 simply runs when the host comes back up.

### What happens when a step fails?

Suppose `ProcessPayment` returns `Fail("Payment gateway error")`.

SagaNet looks at which steps ran before the failure:

```
Completed:  ValidateOrder, ReserveInventory
Failed:     ProcessPayment
```

`ValidateOrder` has no undo action (nothing to roll back). `ReserveInventory` registered a compensation step — `CompensateWith<ReserveInventoryStep>()` — so its `CompensateAsync` is called:

```
Compensation (reverse order):
  ✗ ReserveInventoryStep.CompensateAsync()  ← releases the held stock
```

The workflow ends with status `Compensated`. The customer was never charged, and the warehouse stock is back to normal. No manual cleanup required.

---

## 5. Getting started

### 1. Install packages

```xml
<PackageReference Include="SagaNet.Core" Version="1.0.0" />
<PackageReference Include="SagaNet.Persistence.EfCore" Version="1.0.0" />
<PackageReference Include="SagaNet.Extensions.DependencyInjection" Version="1.0.0" />
```

### 2. Add to your DI setup

```csharp
// Program.cs / Startup.cs

// Persistence (SQL Server)
services.AddDbContext<SagaNetDbContext>(opt =>
    opt.UseSqlServer(Configuration.GetConnectionString("SagaNet")));

services.AddScoped<IWorkflowRepository, EfWorkflowRepository>();

// Register step classes so their constructor dependencies are injected
services.AddTransient<ValidateOrderStep>();
services.AddTransient<ReserveInventoryStep>();
services.AddTransient<ProcessPaymentStep>();
services.AddTransient<ShipOrderStep>();

// Wire up the engine
services.AddSagaNet(builder =>
{
    builder
        .AddWorkflow<OrderWorkflow, OrderData>()
        .Configure(opt =>
        {
            opt.PollInterval            = TimeSpan.FromMilliseconds(500);
            opt.PollBatchSize           = 10;
            opt.MaxConcurrentWorkflows  = 4;
        });
});
```

### 3. Create the database schema

Run EF Core migrations once:

```bash
dotnet ef migrations add InitialCreate --project SagaNet.Persistence.EfCore
dotnet ef database update
```

### 4. Start a workflow from your application code

```csharp
public class OrderController : ControllerBase
{
    private readonly IWorkflowHost _workflows;

    public OrderController(IWorkflowHost workflows) => _workflows = workflows;

    [HttpPost]
    public async Task<IActionResult> PlaceOrder(PlaceOrderRequest req)
    {
        var instanceId = await _workflows.StartWorkflowAsync<OrderWorkflow, OrderData>(
            new OrderData
            {
                OrderId       = req.OrderId,
                Amount        = req.Amount,
                CustomerEmail = req.Email
            });

        return Accepted(new { WorkflowInstanceId = instanceId });
    }
}
```

That's it. The workflow runs entirely in the background. Your controller returns immediately.

---

## 6. Defining steps

### Simple step

```csharp
public class ValidateOrderStep : IWorkflowStep<OrderData>
{
    // Constructor dependencies are resolved from DI automatically.
    private readonly ILogger<ValidateOrderStep> _logger;

    public ValidateOrderStep(ILogger<ValidateOrderStep> logger) => _logger = logger;

    public Task<ExecutionResult> RunAsync(StepExecutionContext<OrderData> context)
    {
        _logger.LogInformation("Validating order {OrderId}", context.Data.OrderId);

        if (string.IsNullOrWhiteSpace(context.Data.OrderId))
            return Task.FromResult(ExecutionResult.Fail("OrderId cannot be empty."));

        return Task.FromResult(ExecutionResult.Next());
    }
}
```

### Compensatable step (saga)

Add `ICompensatableStep<T>` and implement `CompensateAsync`.  
`CompensateAsync` is only ever called during rollback — it should undo whatever `RunAsync` did.

```csharp
public class ReserveInventoryStep : ICompensatableStep<OrderData>
{
    private readonly IInventoryService _inventory;

    public ReserveInventoryStep(IInventoryService inventory) => _inventory = inventory;

    public async Task<ExecutionResult> RunAsync(StepExecutionContext<OrderData> context)
    {
        await _inventory.ReserveAsync(context.Data.OrderId);
        context.Data.InventoryReserved = true;
        return ExecutionResult.Next();
    }

    public async Task CompensateAsync(StepExecutionContext<OrderData> context)
    {
        // Called automatically if a later step fails.
        await _inventory.ReleaseAsync(context.Data.OrderId);
        context.Data.InventoryReserved = false;
    }
}
```

### The step context

The `StepExecutionContext<TData>` object passed to every step gives you:

| Property | What it is |
|---|---|
| `context.Data` | The strongly-typed workflow data. Mutate it freely — changes are saved after the step completes. |
| `context.AttemptCount` | How many times this step has run (0 on the first attempt). Useful for conditional retry logic. |
| `context.CancellationToken` | Cancelled when the host shuts down. Pass it to all async calls. |
| `context.PersistenceData` | A `Dictionary<string, string>` scoped to this step — for saving step-level state across retries. |
| `context.WorkflowInstanceId` | The ID of the running workflow instance. |

---

## 7. Defining a workflow

```csharp
public class OrderWorkflow : IWorkflow<OrderData>
{
    public string Name    => "OrderWorkflow";
    public int    Version => 1;   // optional — defaults to 1

    public void Build(IWorkflowBuilder<OrderData> builder)
    {
        builder
            // First step
            .StartWith<ValidateOrderStep>("ValidateOrder")
                .WithDescription("Checks that all required fields are present.")

            // Second step — with compensation
            .Then<ReserveInventoryStep>("ReserveInventory")
                .CompensateWith<ReserveInventoryStep>()        // undo if something later fails
                .WithRetry(r => r.MaxAttempts(3)               // retry up to 3 times
                                 .InitialDelay(TimeSpan.FromSeconds(2))
                                 .ExponentialBackoff(2.0))     // 2s, 4s, 8s delays

            // Third step — with compensation
            .Then<ProcessPaymentStep>("ProcessPayment")
                .CompensateWith<ProcessPaymentStep>()
                .WithRetry(r => r.MaxAttempts(2))

            // Final step
            .Then<ShipOrderStep>("ShipOrder");
    }
}
```

**Rules:**
- Call `StartWith<>()` exactly once to set the first step.
- Chain `Then<>()` for each subsequent step.
- `CompensateWith<>()` attaches an undo action to the step immediately before it.
- Register the workflow with `.AddWorkflow<OrderWorkflow, OrderData>()` in DI.

---

## 8. What should be a separate step?

This is the most important design question when building workflows. Getting it right keeps your workflow clear, resilient, and observable. Getting it wrong creates either a tangled mess of tiny classes or a single monolithic step that defeats the purpose of the engine.

### Make it a separate step when…

**It calls an external system.**  
Anything that crosses a network boundary — a database write, an HTTP call, a queue message, a third-party API — should be its own step. Each of these can fail independently and benefits from retry and compensation logic.

```
✓ ChargePaymentStep     ← calls payment gateway
✓ SendEmailStep         ← calls email provider
✓ UpdateInventoryStep   ← writes to a separate service
✓ PublishOrderEventStep ← writes to a message bus
```

**It has a meaningful undo action.**  
If the work done by a step needs to be reversed when something later fails, it must be its own step so you can attach a `CompensateWith<>`. A step that does three things can only be rolled back as a whole unit — and that is often too coarse.

```
✓ ReserveInventoryStep + CompensateWith — can release the reservation independently
✓ ChargeCardStep + CompensateWith       — can issue a refund independently
```

**It can fail in a way that is independent of the other steps.**  
If failure here does not imply failure everywhere, it should be separate so that SagaNet can retry just that step without re-running what already succeeded.

**It is a meaningful progress milestone.**  
If someone looking at a dashboard would care whether this specific thing has happened yet, make it a step. Each step has its own status visible in the progress API — `Pending`, `Running`, `Complete`, `Failed`. You get observability for free.

```
✓ "Has the payment been charged yet?"   ← ProcessPaymentStep
✓ "Has the parcel been dispatched yet?" ← ShipOrderStep
```

**It can take a long time or needs to pause.**  
Long-running work and anything that waits for an external event (`WaitForEvent`) must be its own step, because the workflow engine suspends and resumes at step boundaries.

---

### Do NOT make it a separate step when…

**It is a pure in-memory calculation with no side effects.**  
If it only reads and transforms data already in `context.Data`, it belongs inside the same step as the work that uses its result — not as its own step.

```
✗ CalculateDiscountStep   ← just math on existing data, no I/O
✗ BuildEmailBodyStep      ← just string formatting
✗ SetStatusFlagStep       ← just sets context.Data.SomeFlag = true
```

**It always succeeds and can never be compensated.**  
A step with no retry benefit, no compensation action, and no failure mode is just code. Put it in the `RunAsync` of the step it logically belongs to.

**It is too granular to be meaningful on a dashboard.**  
If no one will ever ask "is this specific thing done yet?", the granularity is probably too fine. Steps have overhead — a DB read and write per execution. Don't pay that cost for work that finishes in microseconds and never fails.

**It must share a database transaction with the previous step.**  
SagaNet commits between steps. If two operations must be atomic (either both happen or neither does), they must live in the same step and share a single `DbContext` transaction.

```
✗ separate steps for InsertOrder + InsertOrderLines
  ← these should be one step that writes both in one transaction
```

---

### The rule of thumb

> **One step = one side effect on one external system.**

If a step calls two external systems, split it. If a step does pure logic with no I/O, fold it into its neighbour. If you are unsure whether a failure here would require a different retry or compensation strategy than the surrounding code, that is a signal to split.

**Example — order workflow redesigned:**

| Step | Separate? | Why |
|---|---|---|
| Parse and validate request fields | No | Pure logic, no I/O, always succeeds |
| Write order record to database | Yes | External I/O, can fail, needs compensation (delete) |
| Reserve inventory | Yes | External service, independent failure, needs compensation (release) |
| Calculate shipping cost | No | Pure calculation on already-loaded data |
| Charge payment | Yes | External service, independent failure, needs compensation (refund) |
| Send confirmation email | Yes | External service, independent failure (but no compensation needed — email already sent) |
| Ship order | Yes | External service, final action, progress milestone |

---

## 9. Waiting for an external event

Some workflows need to pause and wait for something to happen outside your application — a payment webhook, a human approval, a delivery confirmation.

**In your step, return `WaitForEvent`:**

```csharp
public class WaitForPaymentConfirmationStep : IWorkflowStep<OrderData>
{
    public Task<ExecutionResult> RunAsync(StepExecutionContext<OrderData> context)
    {
        // Suspend this workflow until the event "PaymentConfirmed"
        // with the key equal to this order's ID arrives.
        return Task.FromResult(
            ExecutionResult.WaitForEvent("PaymentConfirmed", context.Data.OrderId));
    }
}
```

The workflow status becomes `Suspended`. It sits in the database doing nothing until the event arrives.

**When the external system calls back, publish the event:**

```csharp
// e.g. in a webhook controller
await workflowHost.PublishEventAsync(
    eventName: "PaymentConfirmed",
    eventKey:  orderId,
    eventData: new { TransactionId = "TXN-123" });
```

SagaNet wakes up the waiting workflow and resumes from the next step.

---

## 10. Configuring retry behaviour

By default, SagaNet retries any step that throws an unhandled exception. You can configure this globally or per step.

### Global default (applies to all steps)

```csharp
services.AddSagaNet(b => b.Configure(opt =>
{
    opt.DefaultRetryPolicy = new RetryPolicy
    {
        MaxAttempts       = 3,
        InitialDelay      = TimeSpan.FromSeconds(5),
        MaxDelay          = TimeSpan.FromMinutes(10),
        BackoffMultiplier = 2.0    // delays: 5s, 10s, 20s, ...
    };
}));
```

### Per step

```csharp
.Then<ProcessPaymentStep>()
    .WithRetry(r => r
        .MaxAttempts(5)
        .InitialDelay(TimeSpan.FromSeconds(10))
        .MaxDelay(TimeSpan.FromMinutes(5))
        .ExponentialBackoff(1.5))
```

### How retries work

When a step **throws an exception**, the `RetryMiddleware` catches it and decides:

- If attempts < max → returns `Retry(delay)`. The step is scheduled to run again after the computed delay.
- If attempts ≥ max → returns `Fail(reason)`. Compensation is triggered.

When a step **returns `ExecutionResult.Retry(delay)` itself**, it bypasses the exception mechanism and retries directly. This is useful for expected transient conditions (e.g. "resource not ready yet").

---

## 11. Observability — logs, traces, metrics

SagaNet emits structured logs, OpenTelemetry traces, and metrics automatically.

### Logs

Every step execution logs:
- `DEBUG` — step started, step completed with duration
- `INFO` — retry scheduled with delay and attempt count
- `WARN` — step failed with reason
- `ERROR` — unhandled exception or compensation failure

### OpenTelemetry traces

Add to your OTEL setup:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("SagaNet")           // traces every workflow and step
        .AddOtlpExporter());
```

Each workflow run creates a parent span `workflow.<Name>`. Each step creates a child span `step.<StepName>` tagged with:
- `saganet.workflow.name`
- `saganet.step.name`
- `saganet.workflow.instance_id`

### Metrics (OpenTelemetry Meter `SagaNet`)

| Metric | Type | What it counts |
|---|---|---|
| `saganet.workflows.started` | Counter | New workflow instances created |
| `saganet.workflows.completed` | Counter | Workflows that finished successfully |
| `saganet.workflows.compensated` | Counter | Workflows that rolled back |
| `saganet.workflows.failed` | Counter | Workflows that failed permanently |
| `saganet.workflows.active` | UpDownCounter | Currently running instances |
| `saganet.steps.executed` | Counter | Total step executions (all attempts) |
| `saganet.steps.retried` | Counter | Retry attempts |
| `saganet.steps.failed` | Counter | Permanently failed steps |
| `saganet.steps.duration_ms` | Histogram | Step execution time in ms |
| `saganet.workflows.duration_ms` | Histogram | End-to-end workflow time in ms |

### Health check

```csharp
services.AddHealthChecks()
    .AddSagaNetHealthCheck("workflow-engine");
```

Reports `Healthy` when the background host is running, `Degraded` if it has stopped.

---

## 12. Configuration reference

All options live under the `"SagaNet"` section in `appsettings.json`, or set programmatically.

```json
{
  "SagaNet": {
    "PollInterval": "00:00:00.500",
    "PollBatchSize": 10,
    "MaxConcurrentWorkflows": 8,
    "StepTimeout": "00:05:00",
    "EnableInstanceCleanup": true,
    "InstanceRetentionPeriod": "30.00:00:00"
  }
}
```

| Option | Default | Description |
|---|---|---|
| `PollInterval` | 500 ms | How often the host checks for runnable workflow instances |
| `PollBatchSize` | 10 | Max instances fetched per poll cycle |
| `MaxConcurrentWorkflows` | CPU count (max 16) | Worker threads processing workflows in parallel |
| `StepTimeout` | 5 minutes | Max time a single step can run before being cancelled |
| `EnableInstanceCleanup` | false | If true, removes completed instances after the retention period |
| `InstanceRetentionPeriod` | 30 days | How long to keep completed instances before cleanup |

---

## 13. Project structure

```
SagaNet/
│
├── src/
│   │
│   ├── SagaNet.Core                          ← No infrastructure dependencies
│   │   ├── Abstractions/                     ← All interfaces (IWorkflow, IStep, etc.)
│   │   ├── Models/                           ← Data model (WorkflowInstance, ExecutionPointer, etc.)
│   │   ├── Builder/                          ← The fluent .StartWith().Then() DSL
│   │   ├── Engine/                           ← WorkflowHost, WorkflowExecutor, SagaCompensator
│   │   ├── Middleware/                       ← ObservabilityMiddleware, RetryMiddleware
│   │   ├── Events/                           ← In-process event broker
│   │   ├── Registry/                         ← Holds compiled workflow definitions
│   │   ├── Diagnostics/                      ← OpenTelemetry ActivitySource + Meter
│   │   └── Exceptions/                       ← WorkflowException, StepExecutionException
│   │
│   ├── SagaNet.Persistence.EfCore            ← SQL Server storage layer
│   │   ├── SagaNetDbContext.cs               ← EF Core DbContext
│   │   ├── Entities/                         ← DB table mappings
│   │   ├── Configurations/                   ← EF fluent configurations + indexes
│   │   └── Repositories/EfWorkflowRepository ← Implements IWorkflowRepository
│   │
│   └── SagaNet.Extensions.DependencyInjection
│       ├── ServiceCollectionExtensions.cs    ← AddSagaNet() entry point
│       ├── SagaNetBuilder.cs                 ← Fluent builder for AddWorkflow / Configure
│       └── WorkflowHostHealthCheck.cs        ← ASP.NET health check integration
│
├── tests/SagaNet.Tests                       ← xUnit tests (in-memory, no SQL needed)
│
└── samples/SagaNet.Sample                    ← Runnable example: OrderWorkflow saga
```

---

## Quick reference card

```
Define data    →  public class MyData { ... }
Define step    →  class MyStep : IWorkflowStep<MyData>
Define saga    →  class MyStep : ICompensatableStep<MyData>  (adds CompensateAsync)
Define flow    →  class MyWorkflow : IWorkflow<MyData>  { Build(builder) { ... } }
Register       →  services.AddSagaNet(b => b.AddWorkflow<MyWorkflow, MyData>())
Start          →  await host.StartWorkflowAsync<MyWorkflow, MyData>(data)
Resume         →  await host.PublishEventAsync("EventName", "key", payload)
Stop           →  await host.TerminateWorkflowAsync(instanceId)
```
