# TenantContext

Production-ready, composable tenant-context propagation for .NET 8 / ASP.NET Core. Designed for B2B2C SaaS where the same code path must run for many tenants and **isolation must be automatic** across logs, caches, outbox, sagas, and outbound calls.

The library is split into one **tech-agnostic core** + opt-in **adapters**. Pull only what you need.

| Package | Purpose | Dependencies |
| --- | --- | --- |
| `TenantContext` | Accessor (AsyncLocal), resolver pipeline, ambiguity / missing policies, JSON serializer, DI | `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Options` |
| `TenantContext.AspNetCore` | Middleware + header / subdomain / claim / route / query / API-key resolvers | `Microsoft.AspNetCore.App` (framework ref), core |
| `TenantContext.Http` | `DelegatingHandler` that stamps the tenant on outgoing HTTP calls | `Microsoft.Extensions.Http`, core |
| `TenantContext.Grpc` | Client + server gRPC interceptors using call metadata | `Grpc.Core.Api`, core |
| `TenantContext.Logging` | `ILogger.BeginScope` helpers + tenant-aware logger provider | `Microsoft.Extensions.Logging.Abstractions`, core |
| `TenantContext.Tasks` | Snapshot/restore the ambient tenant across queues / channels / custom schedulers | core |

The core package has **no web, transport, EF, or hosting dependencies** — fits inside libraries (caching, outbox, inbox, saga) without forcing consumers to adopt a specific stack.

---

## Design principles

- **One source of truth** — the ambient tenant lives behind `ITenantContextAccessor`. Every consumer (cache key builder, outbox writer, saga repository, structured log enricher) takes a dependency on it; resolution policy lives in one place.
- **Composable resolvers** — resolution is a chain of `ITenantResolver`s ordered by registration. Header → claim → subdomain → static fallback is just five lines.
- **Explicit policies** — what happens when no resolver wins (`AllowEmpty` / `Throw` / `UseDefault`) or when two resolvers disagree (`UseFirst` / `Throw`) is configuration, not behavior buried in code.
- **Transport-agnostic core** — the resolution context is a free-form bag (HTTP, gRPC, message broker stash whatever they have). Adapters never leak into core code.
- **SOLID** — each resolver has a single reason to change; `CompositeTenantResolver` orchestrates without knowing about transports; everything is replaceable via DI.
- **Safe scopes** — `ITenantContextSetter.BeginScope` returns an `IDisposable` that restores the previous tenant, so nesting (e.g. cross-tenant operations during a request) is correct by construction.

---

## Quick start (ASP.NET Core)

```csharp
builder.Services
    .AddTenantContext(o =>
    {
        o.OnMissing  = MissingTenantPolicy.Throw;     // fail fast
        o.OnAmbiguous = AmbiguityPolicy.Throw;        // log & reject conflicts
        o.ShortCircuitOnFirstMatch = false;           // run all resolvers so we can detect ambiguity
    })
    .AddHeaderResolver(o => o.HeaderName = "X-Tenant-Id")
    .AddClaimResolver(o => o.ClaimType = "tid")
    .AddSubdomainResolver(o =>
    {
        o.RootHosts.Add("example.com");
        o.ExcludedSubdomains.Add("www");
    })
    .AddHttpOptions(o =>
    {
        o.WriteFailureResponse = true;                // 400 on resolution exception
        o.ResponseHeader = "X-Tenant-Id";             // echo for debugging
    });

var app = builder.Build();
app.UseAuthentication();   // populates ClaimsPrincipal first if you use ClaimResolver
app.UseRouting();          // populates RouteValues first if you use RouteResolver
app.UseTenantContext();    // <-- here
app.UseAuthorization();
app.MapControllers();
```

Anywhere downstream:

```csharp
public sealed class OrderService(ITenantContextAccessor tenant)
{
    public async Task PlaceAsync(Order o, CancellationToken ct)
    {
        var t = tenant.Current;          // never null; check t.IsResolved
        // use t.TenantId.Value as cache prefix, schema lookup, outbox column, etc.
    }
}
```

---

## Outbound HTTP

```csharp
builder.Services.AddTenantContext().AddHttpPropagation(o => o.HeaderName = "X-Tenant-Id");
builder.Services.AddHttpClient<BillingClient>().AddTenantPropagation();
```

Every call made through `BillingClient` carries the current tenant header — same key, same value, every time. Pair the receiving service with `AddHeaderResolver()` and the chain closes.

---

## gRPC

```csharp
builder.Services.AddTenantContext().AddGrpcServer();   // server-side resolver
// register the interceptor on your gRPC service host:
builder.Services.AddGrpc(o => o.Interceptors.Add<TenantServerInterceptor>());

// outbound:
builder.Services.AddTenantContext().AddGrpcClient();
builder.Services.AddGrpcClient<Billing.BillingClient>(...)
    .AddInterceptor<TenantClientInterceptor>();
```

---

## Background work / outbox / sagas

```csharp
// at enqueue time (still inside the request scope):
public sealed class EnqueueJob(ITenantContextCapturer capturer, ITenantContextSerializer serializer)
{
    public string Persist() => serializer.Serialize(capturer.Capture().Tenant);
}

// at dequeue time, on a worker thread that has no ambient tenant:
public sealed class RunJob(ITenantContextSetter setter, ITenantContextSerializer serializer)
{
    public async Task ProcessAsync(string tenantPayload, Func<Task> work)
    {
        var tenant = serializer.Deserialize(tenantPayload);
        using var _ = tenant.IsResolved ? setter.BeginScope(tenant) : null;
        await work();    // logs, cache, downstream calls all see the right tenant
    }
}
```

This is how downstream libraries (`OutboxNet`, `InboxNet`, `SagaNet`) can scope their data without taking a dependency on any web stack — they only need `ITenantContextAccessor` + `ITenantContextSerializer`.

For schema-per-tenant or row-filter-per-tenant, build your own `ITenantSchemaResolver` / EF Core query filter that consumes `ITenantContextAccessor.Current.TenantId` — kept out of this library so it doesn't force a data-access choice.

---

## Logging

```csharp
builder.Services.AddTenantContext().AddLoggerEnrichment();
```

```csharp
public sealed class Worker(ILogger<Worker> log, ITenantLogScopeFactory scopes)
{
    public async Task RunAsync()
    {
        using var _ = scopes.Begin(log);   // every log entry now carries TenantId + TenantSource
        log.LogInformation("starting work");
    }
}
```

Works with Serilog, NLog, MEL JSON, OpenTelemetry logs — they all surface scopes as structured properties.

---

## Custom resolvers

Implement `ITenantResolver` (or use `DelegateTenantResolver` inline):

```csharp
builder.Services.AddTenantContext()
    .AddResolver("legacy-cookie", (ctx, ct) =>
    {
        var http = ctx.Get<HttpContext>(TenantResolutionContext.HttpContextKey);
        var raw = http?.Request.Cookies["legacy_tenant"];
        return TenantId.TryParse(raw, out var id)
            ? ValueTask.FromResult(TenantResolution.Resolved(id, "legacy-cookie"))
            : ValueTask.FromResult(TenantResolution.Unresolved);
    });
```

---

## Policies

| Setting | Values | Default |
| --- | --- | --- |
| `OnMissing` | `AllowEmpty`, `Throw`, `UseDefault` (+ `DefaultTenant`) | `AllowEmpty` |
| `OnAmbiguous` | `UseFirst`, `Throw` | `UseFirst` |
| `ShortCircuitOnFirstMatch` | `bool` | `true` |
| `TenantIdComparer` | any `StringComparer` | `OrdinalIgnoreCase` |

`Throw` raises `TenantResolutionException` with the candidate sources & values attached — easy to log or translate to a 400.

---

## Versioning & compatibility

- Targets `net8.0`. No preview APIs.
- Public types are documented (`<GenerateDocumentationFile>true</GenerateDocumentationFile>`); breaking changes will follow semver.
- The serializer payload is versioned through its `ContentType` (`application/x-tenant+json;v=1`) so persisted outbox messages remain readable across upgrades.

## License

MIT.
