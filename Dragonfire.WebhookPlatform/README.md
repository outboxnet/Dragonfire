# Dragonfire.WebhookPlatform

An embeddable webhook platform for ASP.NET Core, assembled from three Dragonfire libraries:

| Library | Role |
|---|---|
| **[Dragonfire.Outbox](../Dragonfire.Outbox)** | Transactional outbox + HTTP delivery + HMAC signing for outgoing webhooks |
| **[Dragonfire.Inbox](../Dragonfire.Inbox)** | Transactional inbox + dedup + handler dispatch for incoming webhooks |
| **[Dragonfire.TenantContext](../Dragonfire.TenantContext)** | Per-request tenant resolution that auto-tags every published event |

<img width="1915" height="900" alt="WKépernyőkép 2026-05-07 001332" src="https://github.com/user-attachments/assets/0529ca40-38e4-4b21-9c27-61bce453ce4c" />


Drop these into any existing ASP.NET Core app and you get:

- **Send outbox events** in the same DB transaction as your domain writes
- **Send webhooks** to any number of subscribed endpoints (HMAC-signed, retried, with delivery-attempt audit)
- **Receive webhooks** at `/webhooks/{providerKey}` with idempotent dedup and typed handlers
- A **modern dashboard** showing every outgoing message, every delivery attempt, every incoming message, and every handler run — all linked together through the persisted context

---

## Sample app

`samples/Dragonfire.WebhookPlatform.SampleApp` is a runnable end-to-end demo. It generates `order.created` events and ships each one to **two** subscriptions simultaneously:

1. `https://webhook.site/9e48b0c0-4051-4c20-825e-d47894b5ab1e` — proves the **outgoing** path
2. `http://localhost:5066/webhooks/loopback` — feeds the app's own inbox, proving the **incoming** path

So a single click in the sidebar produces one row in **Outgoing**, two delivery attempts (one per subscription), and — moments later — one row in **Incoming** with a successful handler attempt.

### Run it

```bash
cd Dragonfire.WebhookPlatform/samples/Dragonfire.WebhookPlatform.SampleApp
dotnet run
```

Then visit <http://localhost:5066>. Click **+ Single order** or **+ Burst of 5** in the sidebar; refresh **Overview** / **Outgoing** / **Incoming** to watch the messages land.

The connection string defaults to `(localdb)\MSSQLLocalDB`, database `DragonfireWebhookPlatformSample`. Schemas `app`, `outbox`, and `inbox` are created on first run.

---

## How it works

```
            ┌────────────────────────────────────────────────────────────┐
            │  POST /api/orders   (X-Tenant-Id: demo-tenant)             │
            └──────────────────────────────┬─────────────────────────────┘
                                           │ same DB transaction
                                           ▼
       ┌─────────────────────────┐   ┌────────────────────────┐
       │ AppDbContext.Orders     │   │ OutboxDbContext        │
       │ INSERT Order            │   │ INSERT OutboxMessage   │
       └────────────┬────────────┘   └────────────┬───────────┘
                    └────────── COMMIT ───────────┘
                                  │
                                  ▼
                    ┌──────────────────────────────┐
                    │ OutboxProcessorService       │ background HostedService
                    │  → reads WebhookSubscriptions│
                    │  → POSTs HMAC-signed JSON    │
                    └────────────┬─────────────────┘
                                 │
              ┌──────────────────┼─────────────────────────────┐
              ▼                                                ▼
   webhook.site/9e48b0…                       /webhooks/loopback (this app)
   (logged as DeliveryAttempt)                            │
                                                          ▼
                                          ┌────────────────────────────────┐
                                          │ Loopback signature validator   │
                                          │ Loopback payload mapper        │
                                          │ → InboxDbContext.InboxMessages │
                                          │   INSERT (idempotent)          │
                                          └─────────────┬──────────────────┘
                                                        ▼
                                          ┌────────────────────────────────┐
                                          │ InboxDispatcherService         │
                                          │ → LoopbackOrderHandler         │
                                          │   logs, marks attempt success  │
                                          └────────────────────────────────┘
```

Everything you see on the dashboard pages is read straight out of `OutboxDbContext.OutboxMessages` / `DeliveryAttempts` / `WebhookSubscriptions` and `InboxDbContext.InboxMessages` / `InboxHandlerAttempts` — the libraries do the writing, the platform just visualizes.

---

## Embedding it in your own app

The sample's `Program.cs` is the whole integration. Copy it and adjust:

```csharp
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString));

builder.Services
    .AddTenantContext()
    .AddHeaderResolver(o => o.HeaderName = "X-Tenant-Id")
    .AddStaticFallback(new TenantId("demo-tenant"));

builder.Services
    .AddOutboxNet(o => { o.SchemaName = "outbox"; })
    .UseSqlServerContext<AppDbContext>(connectionString)
    .AddBackgroundProcessor()
    .AddWebhookDelivery();

// Bridge tenant → outbox so PublishAsync auto-tags TenantId.
builder.Services.AddScoped<IOutboxContextAccessor, TenantOutboxContextAccessor>();

builder.Services
    .AddInboxNet(o => { o.SchemaName = "inbox"; })
    .UseSqlServer(connectionString)
    .AddBackgroundDispatcher()
    .AddProvider<MyValidator, MyMapper>("myprovider")
    .AddHandler<MyHandler>(h => h.ForProvider("myprovider").ForEvent("thing.happened"));

var app = builder.Build();

app.UseRouting();
app.UseTenantContext();
app.MapInboxWebhooks();             // POST /webhooks/{providerKey}
app.MapRazorPages();                // dashboard pages
```

Publish an event from anywhere in your code:

```csharp
await using var tx = await db.Database.BeginTransactionAsync();
db.Orders.Add(order);
await db.SaveChangesAsync();
await outbox.PublishAsync("order.created", new { id = order.Id, total = order.Total });
await tx.CommitAsync();
```

The processor wakes immediately (push signal), delivers to every matching `WebhookSubscription`, records each attempt, and retries with exponential backoff on failure. Receivers see standard headers:

```
X-Outbox-Event:           order.created
X-Outbox-Message-Id:      <guid>
X-Outbox-Delivery-Id:     <guid>
X-Outbox-Subscription-Id: <guid>
X-Outbox-Timestamp:       <unix-seconds>
X-Outbox-Signature:       sha256=<hex>      // HMAC-SHA256(body, subscription.Secret)
X-Outbox-Correlation-Id:  <opaque>          // when set
```

---

## Project layout

```
Dragonfire.WebhookPlatform/
├── Directory.Build.props
├── Directory.Packages.props
├── Dragonfire.WebhookPlatform.slnx
├── README.md
└── samples/
    └── Dragonfire.WebhookPlatform.SampleApp/
        ├── Domain/                        ← AppDbContext, Order, tenant→outbox bridge
        ├── Endpoints/OrderEndpoints.cs    ← /api/orders, /api/demo/burst
        ├── Inbox/
        │   ├── LoopbackProvider/          ← signature validator + payload mapper
        │   └── Handlers/                  ← LoopbackOrderHandler
        ├── Pages/                         ← Razor Pages dashboard
        ├── Program.cs                     ← single-file wiring
        └── appsettings.json               ← subscriptions + secrets
```
