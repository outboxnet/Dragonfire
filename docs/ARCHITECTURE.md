# Dragonfire — Reference Architecture

A worked example showing how every library in the Dragonfire suite slots into one
production application, where the interface seams are, and which library implements
which abstraction from which other library.

The diagrams use [Mermaid](https://mermaid.js.org); they render natively on GitHub
and in most modern IDEs.

> Scenario throughout this document: **Acme Billing**, a B2B2C SaaS that ingests
> usage events from upstream providers, stores them per tenant, runs a multi-step
> invoicing saga, fans out webhooks to customer-configured endpoints, and exposes
> a REST + gRPC surface to tenant operators. The codebase mounts every Dragonfire
> library and uses the interface seams to keep them decoupled.

---

## 1. System overview

A hand-rolled "what runs where" picture of the production application.

```mermaid
flowchart TB
    classDef http  fill:#1f6feb20,stroke:#1f6feb,color:#1f6feb
    classDef bg    fill:#2da04420,stroke:#2da044,color:#2da044
    classDef store fill:#bf872220,stroke:#bf8722,color:#bf8722
    classDef ext   fill:#8b949e20,stroke:#8b949e,color:#8b949e

    subgraph Edge["🌐 Edge"]
        REST["REST API<br/>(controllers + minimal API)"]:::http
        GRPC["gRPC services"]:::http
        WHIN["Webhook receiver"]:::http
    end

    subgraph App["🏗️ Acme.Billing.App (single host)"]
        direction TB
        subgraph Pipeline["Request pipeline"]
            TENM["TenantContext middleware"]
            FEAT["FeatureGate filter / endpoint filter"]
            LOGM["Dragonfire.Logging proxy"]
            CTRL["Controllers / endpoints"]
        end

        subgraph BGSection["Hosted services"]
            INBOX["Inbox processor<br/>(IInboxProcessor)"]:::bg
            OUTBOX["Outbox processor<br/>(IOutboxProcessor)"]:::bg
            SAGA["Saga host<br/>(IWorkflowHost)"]:::bg
            POLL["Poller workers"]:::bg
            SYNC["Sync orchestrator"]:::bg
            FREF["Feature refresh"]:::bg
            INVW["Cache invalidation worker"]:::bg
        end

        subgraph CacheStack["Caching"]
            CSVC["ICacheService"]
            HYB["Hybrid: L1 memory ↔ L2 Redis"]
            TAGS["Redis tag index"]
        end

        subgraph Outbound["Outbound clients"]
            HCLIENT["HttpClient<br/>(tenant DelegatingHandler)"]
            GCLIENT["gRPC channels<br/>(tenant interceptor)"]
        end
    end

    subgraph Stores["💾 Stores"]
        DB[("SQL Server<br/>app + outbox + inbox<br/>+ saga + features")]:::store
        REDIS[("Redis<br/>cache L2 + tag index")]:::store
        QUEUE[("Azure Storage Queue<br/>(optional outbox transport)")]:::store
    end

    subgraph Outside["🌍 Outside the boundary"]
        UPSTREAM["Upstream providers<br/>(usage, payments)"]:::ext
        CUSTHOOKS["Customer webhook endpoints"]:::ext
        APPINS["Application Insights"]:::ext
    end

    REST  --> TENM
    GRPC  --> TENM
    WHIN  --> TENM
    TENM --> FEAT --> LOGM --> CTRL

    CTRL  --> CSVC
    CTRL  --> SAGA
    CTRL  --> OUTBOX
    CTRL  --> HCLIENT
    CTRL  --> GCLIENT

    INBOX --> DB
    INBOX --> CTRL

    OUTBOX --> DB
    OUTBOX --> QUEUE
    OUTBOX --> CUSTHOOKS

    SAGA  --> DB
    SAGA  --> OUTBOX
    SAGA  --> CSVC

    POLL  --> UPSTREAM
    POLL  --> SAGA

    SYNC  --> UPSTREAM
    SYNC  --> DB

    FREF  --> DB
    FREF  --> CSVC

    INVW  --> CSVC
    CSVC  --> HYB --> REDIS
    HYB   --> TAGS

    LOGM  --> APPINS
    INBOX --> APPINS
    OUTBOX --> APPINS
    POLL  --> APPINS
```

---

## 2. Interface composition graph

This is the heart of the diagram. Every Dragonfire library is built around one or
two **abstractions** that another library can **implement** to bind them together
in DI. Solid arrows point from `interface` → `implementation`. Dashed arrows mean
"is decorated by".

```mermaid
flowchart LR
    classDef ifc   fill:#1f6feb20,stroke:#1f6feb,color:#1f6feb,stroke-dasharray: 0
    classDef impl  fill:#2da04420,stroke:#2da044,color:#2da044
    classDef host  fill:#bf872220,stroke:#bf8722,color:#bf8722
    classDef store fill:#8957e520,stroke:#8957e5,color:#8957e5

    %% ====== TenantContext core ======
    ITCA["ITenantContextAccessor"]:::ifc
    ITCS["ITenantContextSetter"]:::ifc
    ITRES["ITenantResolutionPipeline"]:::ifc
    ALTC["AsyncLocalTenantContext"]:::impl

    ALTC --> ITCA
    ALTC --> ITCS

    %% ====== Features ======
    IFR["IFeatureResolver"]:::ifc
    IFC["IFeatureContextAccessor"]:::ifc
    IFS["IFeatureSource"]:::ifc
    IFAL["IFeatureAuditLog"]:::ifc
    IFST["IFeatureStore"]:::ifc

    DFR["DefaultFeatureResolver"]:::impl
    CFR["CachingFeatureResolver<br/>(decorator)"]:::impl
    HFCA["HttpContextFeatureContextAccessor"]:::impl
    TENFCA["TenantFeatureContextAccessor<br/>(your adapter)"]:::impl
    CFS["ConfigurationFeatureSource"]:::impl
    EFFS["EfCoreFeatureSource"]:::impl
    NOPAL["NoOpFeatureAuditLog"]:::impl
    EFAL["EfCoreFeatureAuditLog"]:::impl
    IMFS["InMemoryFeatureStore"]:::impl

    DFR --> IFR
    CFR -. decorates .-> DFR
    CFR --> IFR

    HFCA --> IFC
    TENFCA --> IFC
    TENFCA -. reads .-> ITCA

    CFS --> IFS
    EFFS --> IFS
    NOPAL --> IFAL
    EFAL --> IFAL
    IMFS --> IFST

    %% ====== Caching ======
    ICS["ICacheService"]:::ifc
    ICP["ICacheProvider"]:::ifc
    ITI["ITagIndex"]:::ifc
    IIQ["IInvalidationQueue"]:::ifc

    CS["CacheService"]:::impl
    MEMP["MemoryCacheProvider"]:::impl
    DISTP["DistributedCacheProvider"]:::impl
    HYBP["HybridCacheProvider"]:::impl
    INMTI["InMemoryTagIndex"]:::impl
    REDTI["RedisTagIndex"]:::impl
    CHANIQ["ChannelInvalidationQueue"]:::impl

    CS --> ICS
    HYBP --> ICP
    MEMP --> ICP
    DISTP --> ICP
    REDTI --> ITI
    INMTI --> ITI
    CHANIQ --> IIQ

    CFR --> ICS
    EFAL -. could log to .-> ICS

    %% ====== Logging ======
    IDLS["IDragonfireLoggingService"]:::ifc
    DLPROXY["[Loggable] generated proxy"]:::impl
    DLPROXY --> IDLS
    DLPROXY -. enriched by .-> ITCA

    %% ====== Outbox ======
    IOPB["IOutboxProcessor"]:::ifc
    IMP["IMessagePublisher"]:::ifc
    IOCA["IOutboxContextAccessor"]:::ifc
    ITSR["ITenantSecretRetriever"]:::ifc
    ISR["ISubscriptionReader"]:::ifc

    OPS["OutboxProcessorService"]:::host
    HMP["HttpMessagePublisher"]:::impl
    QMP["AzureQueueMessagePublisher"]:::impl
    HOCA["HttpContextOutboxContextAccessor"]:::impl
    TENOCA["TenantOutboxContextAccessor<br/>(your adapter)"]:::impl
    CSR["ConfigurationTenantSecretRetriever"]:::impl

    OPS --> IOPB
    HMP --> IMP
    QMP --> IMP
    HOCA --> IOCA
    TENOCA --> IOCA
    TENOCA -. reads .-> ITCA
    CSR --> ITSR

    %% ====== Inbox ======
    IIH["IInboxHandler"]:::ifc
    IIP["IInboxProcessor"]:::ifc
    IWP["IWebhookProvider"]:::ifc
    IWSV["IWebhookSignatureValidator"]:::ifc
    IPS["InboxProcessorService"]:::host

    IPS --> IIP
    %% your handlers
    HND1["YourInboxHandler"]:::impl
    HND1 --> IIH

    %% ====== Saga ======
    IWH["IWorkflowHost"]:::ifc
    IWREP["IWorkflowRepository"]:::ifc
    IEB["IEventBroker"]:::ifc
    IWMID["IWorkflowMiddleware"]:::ifc

    WHHOST["WorkflowHost"]:::host
    EFREP["EfCoreWorkflowRepository"]:::impl
    OBSMW["ObservabilityMiddleware"]:::impl
    RETMW["RetryMiddleware"]:::impl

    WHHOST --> IWH
    EFREP --> IWREP
    OBSMW --> IWMID
    RETMW --> IWMID

    %% ====== Sync ======
    ISO["ISyncOrchestrator"]:::ifc
    ISR2["ISyncRunner"]:::ifc
    ISSS["ISyncStateStore"]:::ifc
    ISDH["ISyncDataHandler"]:::ifc

    SOH["SyncOrchestrator"]:::host
    SOH --> ISO

    %% ====== Poller ======
    IPO["IPollingOrchestrator"]:::ifc
    IPC["IPollingCondition"]:::ifc
    IPMT["IPollingMetricsTracker"]:::ifc

    POSVC["PollingService"]:::host
    POSVC --> IPO

    %% ====== Cross-library glue ======
    classDef hub fill:#f78166,stroke:#f78166,color:#000,font-weight:bold
    HUB(("ITenantContextAccessor<br/>is the central seam")):::hub
    ITCA --- HUB
    HUB -. used by .-> TENFCA
    HUB -. used by .-> TENOCA
    HUB -. used by .-> DLPROXY
    HUB -. used by .-> CFR
```

**The seams to remember:**

| Abstraction | Defined in | Implementations in this app |
|---|---|---|
| `ITenantContextAccessor` | `Dragonfire.TenantContext` | `AsyncLocalTenantContext` (default) |
| `IFeatureContextAccessor` | `Dragonfire.Features` | `HttpContextFeatureContextAccessor` (default) **OR** `TenantFeatureContextAccessor` (your adapter, reads `ITenantContextAccessor.Current`) |
| `IFeatureResolver` | `Dragonfire.Features` | `DefaultFeatureResolver` ⇽ decorated by `CachingFeatureResolver` (uses `ICacheService`) |
| `IFeatureSource` | `Dragonfire.Features` | `ConfigurationFeatureSource` + `EfCoreFeatureSource` (later wins on collision) |
| `IFeatureAuditLog` | `Dragonfire.Features` | `EfCoreFeatureAuditLog` |
| `IOutboxContextAccessor` | `Dragonfire.Outbox` | `HttpContextOutboxContextAccessor` **OR** `TenantOutboxContextAccessor` (reads `ITenantContextAccessor.Current`) |
| `IMessagePublisher` | `Dragonfire.Outbox` | `HttpMessagePublisher` (signs + posts) or `AzureQueueMessagePublisher` |
| `ICacheProvider` | `Dragonfire.Caching` | `HybridCacheProvider` ⇽ chains `MemoryCacheProvider` + `DistributedCacheProvider` |
| `ITagIndex` | `Dragonfire.Caching` | `RedisTagIndex` |
| `IWorkflowRepository` | `Dragonfire.Saga` | `EfCoreWorkflowRepository` |

The italicised "your adapter" rows are the integration glue you write in
`Acme.Billing.App` — typically 5–15 lines each. They are shown verbatim in §6.

---

## 3. Startup composition (`Program.cs`)

This is the order in which the libraries fall into place. The `using` blocks are
elided for brevity; every method is a real DI extension shipped by Dragonfire.

```csharp
var builder = WebApplication.CreateBuilder(args);

// ─── 1. Cross-cutting: tenant + logging come first so everything else
//                    can take a dependency on ITenantContextAccessor.
builder.Services
    .AddTenantContext()
        .AddHeaderResolver("X-Tenant-Id")
        .AddClaimResolver("tenant_id")
        .AddSubdomainResolver()
        .AddStaticFallback(TenantId.Empty);            // explicit "no tenant" fallback

builder.Services.AddDragonfireLogging(o =>
{
    o.RedactSensitiveData = true;
});
builder.Services.AddDragonfireTenantLogging();          // LoggingEnricher → ITenantContextAccessor

// ─── 2. Caching: hybrid memory + Redis, Redis-backed tag index.
builder.Services
    .AddDragonfireCaching()
    .AddHybridProvider(opt =>
    {
        opt.L1.Sizing = MemorySizing.Medium;
        opt.L2.ConnectionString = builder.Configuration["Redis"];
    })
    .AddRedisTagIndex(builder.Configuration["Redis"]!);
builder.Services.AddDragonfireGeneratedCaching();        // [Cache]/[CacheInvalidate] proxies

// ─── 3. Features: configuration + EF source, Caching decorator,
//                  AspNetCore filter, tenant-aware context accessor.
builder.Services.AddDragonfireFeatures(o => o.RefreshInterval = TimeSpan.FromSeconds(15));
builder.Services.AddDragonfireFeaturesConfiguration(builder.Configuration);
builder.Services.AddDragonfireFeaturesEntityFrameworkCore<AppDbContext>();
builder.Services.AddDragonfireFeaturesAspNetCore();
builder.Services.AddDragonfireFeaturesCaching(o => o.Ttl = TimeSpan.FromMinutes(1));

// Replace HttpContextFeatureContextAccessor with the tenant-aware adapter:
builder.Services.RemoveAll<IFeatureContextAccessor>();
builder.Services.AddSingleton<IFeatureContextAccessor, TenantFeatureContextAccessor>();

// ─── 4. Outbox: EF Core + SQL Server + HTTP delivery + tenant context.
builder.Services
    .AddOutboxNet()
    .UseEntityFrameworkCore<AppDbContext>()
    .UseSqlServerProcessor()
    .UseHttpMessagePublisher()
    .UseTenantSecretRetriever<DbTenantSecretRetriever>()
    .UseOutboxContextAccessor<TenantOutboxContextAccessor>();

// ─── 5. Inbox: EF Core + AspNetCore receiver + Stripe-style provider.
builder.Services
    .AddInboxNet()
    .UseEntityFrameworkCore<AppDbContext>()
    .AddProvider<StripeWebhookProvider>()
    .AddHandler<UsageEventHandler>()
    .ConfigureRetry(p => p.MaxAttempts = 8);

// ─── 6. Saga: EF Core persistence + retry + observability middleware.
builder.Services
    .AddSagaNet()
    .UseEntityFrameworkCorePersistence<AppDbContext>()
    .AddWorkflow<InvoiceSagaDefinition>()
    .AddMiddleware<ObservabilityMiddleware>()
    .AddMiddleware<RetryMiddleware>();

// ─── 7. Sync: per-stream pollers for upstream APIs.
builder.Services
    .AddSyncLibrary()
    .AddSyncStream<UsageProviderClient, UsageDto>(s => s.Interval = TimeSpan.FromMinutes(5));

// ─── 8. Poller: ad-hoc polling jobs (e.g. waiting on payments).
builder.Services
    .AddPolling<PaymentStatusRequest, PaymentStatusResponse>()
    .UsePersistence<EfCorePollingRepository>();

// ─── 9. AspNet pipeline.
builder.Services.AddControllers(o => o.Filters.AddService<FeatureGateActionFilter>());
builder.Services.AddGrpc(o => o.Interceptors.Add<TenantServerInterceptor>());

// ─── 10. Outbound HttpClient stamps tenant on every outgoing call.
builder.Services
    .AddHttpClient("upstream")
    .AddTenantPropagation();                            // DelegatingHandler from TenantContext.Http

var app = builder.Build();

app.UseTenantContext();                                  // sets AsyncLocal for the request
app.UseRouting();
app.MapControllers();
app.MapGet("/health", () => "ok").RequireFeature("HealthcheckV2");

app.Run();
```

---

## 4. Inbound request lifecycle

What happens when a customer hits `POST /tenants/acme/invoices` with
`X-Tenant-Id: acme`. The diagram only includes the libraries that participate.

```mermaid
sequenceDiagram
    autonumber
    participant Client
    participant Kestrel
    participant TenantMW as Tenant<br/>middleware
    participant FeatFilter as FeatureGate<br/>filter
    participant LogProxy as [Loggable]<br/>proxy
    participant Ctrl as InvoicesController
    participant Cache as ICacheService<br/>(Hybrid)
    participant Saga as IWorkflowHost
    participant DB
    participant Outbox as Outbox table
    participant OutboxSvc as OutboxProcessor

    Client->>Kestrel: POST /invoices (X-Tenant-Id: acme)
    Kestrel->>TenantMW: HttpContext
    TenantMW->>TenantMW: ITenantResolutionPipeline.ResolveAsync
    TenantMW->>TenantMW: ITenantContextSetter.Set(TenantInfo("acme", ...))
    Note over TenantMW: AsyncLocal scoped to the request

    TenantMW->>FeatFilter: next()
    FeatFilter->>FeatFilter: IFeatureResolver.IsEnabledAsync("InvoicingV2")
    Note right of FeatFilter: Caching decorator hits ICacheService<br/>key=features:InvoicingV2:acme:user-42

    FeatFilter->>LogProxy: next()
    LogProxy->>Ctrl: CreateInvoice(req)
    Note right of LogProxy: enriched scope: tenant=acme,<br/>correlationId=…

    Ctrl->>Cache: GetOrAddAsync("tenant:acme:profile", …)
    Cache-->>Ctrl: TenantProfile
    Ctrl->>Saga: StartWorkflowAsync<InvoiceSaga>(data)
    Saga->>DB: persist WorkflowInstance + Outbox row<br/>(SAME transaction)
    DB-->>Saga: ok
    Saga-->>Ctrl: InvoiceId
    Ctrl-->>Client: 202 Accepted

    Note over OutboxSvc: hot-path channel signal

    OutboxSvc->>DB: SELECT … FOR UPDATE next batch
    OutboxSvc->>OutboxSvc: IOutboxContextAccessor.Current<br/>(tenant=acme)
    OutboxSvc->>OutboxSvc: ITenantSecretRetriever → HMAC key for "acme"
    OutboxSvc->>Client: POST customer-webhook (signed)
```

**Where each library shows up in the trace:**

| Step | Library | Interface |
|---|---|---|
| 3–4 | `Dragonfire.TenantContext.AspNetCore` | `ITenantResolutionPipeline`, `ITenantContextSetter` |
| 5 | `Dragonfire.Features.AspNetCore` | `FeatureGateActionFilter` → `IFeatureResolver` |
| 6 | `Dragonfire.Features.Caching` | `CachingFeatureResolver` → `ICacheService` |
| 7 | `Dragonfire.Logging` | `[Loggable]`-generated proxy → `IDragonfireLoggingService` |
| 9 | `Dragonfire.Caching` | `ICacheService.GetOrAddAsync` |
| 10–11 | `Dragonfire.Saga` + `Dragonfire.Outbox` | `IWorkflowHost`, transactional outbox row |
| 13–17 | `Dragonfire.Outbox` | `OutboxProcessorService` reads, signs, delivers |

---

## 5. Background workers

Every Dragonfire library that needs to "tick" registers a hosted service. They
share `IServiceScopeFactory` for per-execution scopes and (where available) the
`ITenantContextAccessor` ambient when the work is per-tenant.

```mermaid
flowchart LR
    classDef host fill:#bf872220,stroke:#bf8722,color:#bf8722
    classDef ifc  fill:#1f6feb20,stroke:#1f6feb,color:#1f6feb

    subgraph BG["IHostedService"]
        FREF["FeatureRefreshHostedService<br/>(PeriodicTimer, every 15s)"]:::host
        OPS["OutboxProcessorService<br/>(hot-path channel + cold scan)"]:::host
        IPS["InboxProcessorService<br/>(channel + adaptive poll)"]:::host
        WHH["WorkflowHost<br/>(saga runnable scan)"]:::host
        SOH["SyncOrchestrator<br/>(per-stream PeriodicTimer)"]:::host
        POLL["PollingService<br/>(ad-hoc, condition-driven)"]:::host
        INVW["InvalidationWorker<br/>(channel drain)"]:::host
    end

    subgraph Reads["Reads from"]
        IFS["IFeatureSource (config + EF)"]:::ifc
        IWREP["IWorkflowRepository (EF)"]:::ifc
        IIQ["IInvalidationQueue (channel)"]:::ifc
        ISSS["ISyncStateStore (EF)"]:::ifc
        IPCO["IPollingCondition + repo"]:::ifc
        DBOX["Outbox table"]
        DBIN["Inbox table"]
    end

    subgraph Writes["Writes to / through"]
        IFST["IFeatureStore + IFeatureAuditLog"]:::ifc
        IMP["IMessagePublisher"]:::ifc
        IIH["IInboxHandler"]:::ifc
        IWMID["IWorkflowMiddleware chain"]:::ifc
        ITI["ITagIndex (cache eviction)"]:::ifc
        ISDH["ISyncDataHandler (your code)"]:::ifc
    end

    FREF --> IFS --> IFST
    OPS  --> DBOX --> IMP
    IPS  --> DBIN --> IIH
    WHH  --> IWREP --> IWMID
    SOH  --> ISSS --> ISDH
    POLL --> IPCO
    INVW --> IIQ --> ITI
```

---

## 6. The integration adapters you write

These are the only "glue" classes you author. They are short — each one bridges
two abstractions that Dragonfire deliberately keeps independent.

### 6.1 `TenantFeatureContextAccessor` (TenantContext → Features)

```csharp
public sealed class TenantFeatureContextAccessor : IFeatureContextAccessor
{
    private readonly ITenantContextAccessor _tenant;
    private readonly IHttpContextAccessor   _http;

    public TenantFeatureContextAccessor(
        ITenantContextAccessor tenant,
        IHttpContextAccessor http)
    {
        _tenant = tenant;
        _http   = http;
    }

    public FeatureContext Current
    {
        get
        {
            var tenant = _tenant.Current;
            var userId = _http.HttpContext?.User
                .FindFirst(ClaimTypes.NameIdentifier)?.Value;

            return new FeatureContext(
                tenantId:   tenant.IsResolved ? tenant.TenantId.Value : null,
                userId:     userId,
                attributes: tenant.Properties);
        }
    }
}
```

### 6.2 `TenantOutboxContextAccessor` (TenantContext → Outbox)

```csharp
public sealed class TenantOutboxContextAccessor : IOutboxContextAccessor
{
    private readonly ITenantContextAccessor _tenant;
    public TenantOutboxContextAccessor(ITenantContextAccessor tenant) => _tenant = tenant;

    public OutboxContext Current
        => new(TenantId: _tenant.Current.TenantId.Value,
               UserId:   /* from claims if you need it */ null);
}
```

### 6.3 `DbTenantSecretRetriever` (your storage → Outbox HMAC keys)

```csharp
public sealed class DbTenantSecretRetriever : ITenantSecretRetriever
{
    private readonly IServiceScopeFactory _scope;
    public DbTenantSecretRetriever(IServiceScopeFactory scope) => _scope = scope;

    public async Task<string> GetWebhookSecretAsync(
        string tenantId, CancellationToken ct)
    {
        await using var s = _scope.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.WebhookSecrets
            .Where(w => w.TenantId == tenantId)
            .Select(w => w.Secret)
            .SingleAsync(ct);
    }
}
```

That is the full set. Every other interface is implemented by a Dragonfire
library; the application only writes the three adapters above plus its own
business handlers (`UsageEventHandler`, `InvoiceSagaDefinition`, etc.).

---

## 7. Tenant propagation across boundaries

Multi-tenant applications drift fastest when tenant context is lost between
processes. `Dragonfire.TenantContext` ships an adapter for every outbound seam.

```mermaid
flowchart LR
    classDef in  fill:#1f6feb20,stroke:#1f6feb,color:#1f6feb
    classDef out fill:#2da04420,stroke:#2da044,color:#2da044
    classDef async fill:#bf872220,stroke:#bf8722,color:#bf8722

    subgraph Inbound["Inbound (sets AsyncLocal)"]
        IM["AspNetCore middleware"]:::in
        GS["gRPC server interceptor"]:::in
        WB["Webhook receiver<br/>(via Inbox)"]:::in
    end

    ALTC["AsyncLocalTenantContext<br/>= ITenantContextAccessor.Current"]

    subgraph Outbound["Outbound (reads AsyncLocal)"]
        HH["HttpClient DelegatingHandler<br/>→ X-Tenant-Id header"]:::out
        GC["gRPC client interceptor<br/>→ metadata"]:::out
        OBX["Outbox row stamped with TenantId"]:::out
        LOG["Logging enricher<br/>(tenant on every entry)"]:::out
    end

    subgraph CrossTask["Cross-task / queue"]
        CAP["ITenantContextCapturer<br/>(Tasks package)"]:::async
        SER["JsonTenantContextSerializer<br/>(queue payloads)"]:::async
    end

    IM  --> ALTC
    GS  --> ALTC
    WB  --> ALTC

    ALTC --> HH
    ALTC --> GC
    ALTC --> OBX
    ALTC --> LOG

    ALTC --> CAP
    CAP  --> ALTC
    ALTC --> SER
    SER  --> ALTC
```

The `ITenantContextCapturer` is what you call before `Task.Run` /
`Channel.Writer.WriteAsync` so the worker re-establishes the same tenant. The
`JsonTenantContextSerializer` is for crossing process boundaries via queues —
e.g. enqueueing a job to Azure Storage Queue from the outbox.

---

## 8. Cache key + tag taxonomy

A consistent key + tag scheme is what makes invalidation across libraries safe.
Every Dragonfire-aware piece of code follows the same prefix discipline.

```mermaid
flowchart TB
    classDef key  fill:#1f6feb20,stroke:#1f6feb,color:#1f6feb
    classDef tag  fill:#bf872220,stroke:#bf8722,color:#bf8722
    classDef inv  fill:#f7816620,stroke:#f78166,color:#f78166

    subgraph Keys["Cache keys"]
        K1["features:{name}:{tenant}:{user}"]:::key
        K2["tenant:{id}:profile"]:::key
        K3["pricing:{tenant}:{plan}"]:::key
    end

    subgraph Tags["Tags applied"]
        T1["features:{name}"]:::tag
        T2["features-tenant:{tenantId}"]:::tag
        T3["tenant:{id}"]:::tag
    end

    subgraph Triggers["Invalidation triggers"]
        F1["FeatureRefreshHostedService<br/>diff: feature updated"]:::inv
        F2["Inbox handler<br/>(tenant profile changed)"]:::inv
        F3["Saga compensation<br/>(invoice rolled back)"]:::inv
    end

    K1 --- T1
    K1 --- T2
    K2 --- T3
    K3 --- T3

    F1 -. "InvalidateByTagAsync('features:NewCheckout')" .-> T1
    F2 -. "InvalidateByTagAsync('tenant:acme')" .-> T3
    F3 -. "RemoveByPatternAsync('pricing:acme:*')" .-> K3
```

---

## 9. The 39 + 5 = 44 packages, mapped to layers

Where every package fits in the architecture. Everything below ships from this
mono-repo at a single `<Version>`.

```mermaid
flowchart TB
    classDef core fill:#1f6feb20,stroke:#1f6feb,color:#1f6feb
    classDef integ fill:#2da04420,stroke:#2da044,color:#2da044
    classDef provider fill:#bf872220,stroke:#bf8722,color:#bf8722

    subgraph CoreLayer["Cross-cutting core"]
        TC["Dragonfire.TenantContext"]:::core
        LOG["Dragonfire.Logging"]:::core
        CACHE["Dragonfire.Caching"]:::core
        FEAT["Dragonfire.Features"]:::core
    end

    subgraph WorkflowLayer["Workflow & messaging core"]
        IN["Dragonfire.Inbox.Core"]:::core
        OUT["Dragonfire.Outbox.Core"]:::core
        SAGA["Dragonfire.Saga.Core"]:::core
        SYNC["Dragonfire.Sync.Core"]:::core
        POLL["Dragonfire.Poller"]:::core
    end

    subgraph Web["Web integrations"]
        TC1["TenantContext.AspNetCore"]:::integ
        TC2["TenantContext.Grpc"]:::integ
        TC3["TenantContext.Http"]:::integ
        LOG1["Logging.AspNetCore"]:::integ
        LOG2["Logging.Grpc"]:::integ
        FEAT1["Features.AspNetCore"]:::integ
        IN1["Inbox.AspNetCore"]:::integ
        CACHE1["Caching.Grpc"]:::integ
    end

    subgraph Persist["Persistence integrations"]
        FEAT2["Features.EntityFrameworkCore"]:::integ
        IN2["Inbox.EntityFrameworkCore"]:::integ
        OUT2["Outbox.EntityFrameworkCore"]:::integ
        OUT3["Outbox.SqlServer"]:::integ
        SAGA2["Saga.Persistence.EfCore"]:::integ
        SYNC2["Sync.EntityFrameworkCore"]:::integ
    end

    subgraph Providers["Cache providers + serializers"]
        CMEM["Caching.Memory"]:::provider
        CDIST["Caching.Distributed"]:::provider
        CHYB["Caching.Hybrid"]:::provider
        CRED["Caching.Redis"]:::provider
        CPB["Caching.Serialization.Protobuf"]:::provider
        CGEN["Caching.Generator"]:::provider
        LGEN["Logging.Generator"]:::provider
    end

    subgraph Config["Configuration sources & cross-cutting"]
        FEATCFG["Features.Configuration"]:::integ
        FEATCAC["Features.Caching"]:::integ
        TCLOG["TenantContext.Logging"]:::integ
        TCTASK["TenantContext.Tasks"]:::integ
        LOGAI["Logging.ApplicationInsights"]:::integ
    end

    subgraph Transports["Transports"]
        IN3["Inbox.Providers"]:::integ
        IN4["Inbox.Processor"]:::integ
        IN5["Inbox.AzureFunctions"]:::integ
        OUT4["Outbox.Processor"]:::integ
        OUT5["Outbox.Delivery"]:::integ
        OUT6["Outbox.AzureStorageQueue"]:::integ
        OUT7["Outbox.AzureFunctions"]:::integ
        SAGA3["Saga.Extensions.DependencyInjection"]:::integ
        SYNC1["Sync.Abstractions"]:::integ
    end

    CoreLayer --> Web
    CoreLayer --> Persist
    CoreLayer --> Config
    WorkflowLayer --> Persist
    WorkflowLayer --> Transports
    CACHE --> Providers
```

---

## 10. Reading order for new contributors

1. **TenantContext** — every other library reads `ITenantContextAccessor`. Start
   here.
2. **Logging** — pairs with TenantContext via `TenantContext.Logging` enricher.
   Source-generated proxies are non-obvious; read `Generator` first.
3. **Caching** — `ICacheService` + provider stack. Hybrid is the default path in
   prod.
4. **Features** — built on Caching, Configuration, EF Core. Designed to be
   replaced bit by bit (custom `IFeatureSource`, custom `IFeatureContextAccessor`).
5. **Inbox + Outbox** — at-least-once delivery on top of EF Core transactions.
   The hot-path channel is the interesting bit.
6. **Saga** — uses Outbox transactionally for "publish on commit". Workflow
   middleware chain is the extension point.
7. **Sync** — periodic API pulls, separate from Poller (which is request/response).
8. **Poller** — orchestrates request/response polling with backoff. Use this for
   "wait until external job finishes".

---

*This document is the architecture entry point. For per-library deep-dives,
open the README.md inside each package's folder.*
