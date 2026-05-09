# Dragonfire.IdempotentApi

Pluggable, SOLID idempotent HTTP request handling for ASP.NET Core. Provides safe-retry
semantics on top of any storage backend — clients send an `Idempotency-Key` header, the
middleware caches the first response, and duplicate requests get the cached answer
instead of re-executing the handler.

## Why

Networks fail mid-call. Mobile clients reconnect and retry. Background jobs hit
deduplication windows. Without server-side idempotency every retry is a roll of the
dice — duplicate orders, double-charged cards, two welcome emails. The Stripe-style
`Idempotency-Key` header solves the problem at the HTTP layer: same key + same body
within the TTL window returns the cached response; different body returns 422.

## Packages

| Package | Purpose |
|---|---|
| `Dragonfire.IdempotentApi.Core` | Abstractions, models, options, builder. No HTTP dependency. |
| `Dragonfire.IdempotentApi.AspNetCore` | Middleware, default key reader, SHA-256 body fingerprint, response recorder, method/attribute policies. |
| `Dragonfire.IdempotentApi.InMemory` | Process-local store. Tests, dev, single-instance. |
| `Dragonfire.IdempotentApi.EntityFrameworkCore` | Durable EF Core store. Multi-instance safe via PK uniqueness. |

## Quickstart

```csharp
builder.Services
    .AddIdempotentApi(o =>
    {
        o.HeaderName = "Idempotency-Key";
        o.DefaultExpiration = TimeSpan.FromHours(24);
    })
    .AddAspNetCore()
    .UseInMemoryStore();   // or .UseEntityFrameworkCore<MyDbContext>()

var app = builder.Build();
app.UseRouting();
app.UseIdempotentApi();
app.MapPost("/orders", () => Results.Created("/orders/1", new { id = 1 }));
app.Run();
```

```bash
# First call — runs the handler
curl -X POST http://localhost:5000/orders \
     -H 'Idempotency-Key: 7c2e' -H 'Content-Type: application/json' -d '{}'
# → 201 Created  {"id":1}

# Same key, same body — returns cached response with replay marker
curl -X POST http://localhost:5000/orders \
     -H 'Idempotency-Key: 7c2e' -H 'Content-Type: application/json' -d '{}'
# → 201 Created  {"id":1}   Idempotent-Replay: true

# Same key, different body — 422
curl -X POST http://localhost:5000/orders \
     -H 'Idempotency-Key: 7c2e' -H 'Content-Type: application/json' -d '{"x":1}'
# → 422 Unprocessable Entity
```

## Behavior matrix

| Situation | Outcome |
|---|---|
| First request with key K | Handler runs. Response captured, stored under K, returned. |
| Duplicate K, same body, original completed | Stored response returned, with `Idempotent-Replay: true`. |
| Duplicate K, same body, original still running | `409 Conflict`. |
| Duplicate K, different body | `422 Unprocessable Entity`. |
| Method/policy doesn't match | Pass-through, middleware no-ops. |
| No header, `MissingKeyBehavior = Bypass` | Pass-through. |
| No header, `MissingKeyBehavior = RequireKey` | `400 Bad Request`. |
| Handler throws | Reservation released so the next retry can succeed. Exception propagates. |

## Architecture

```
                ┌────────────────────────────────────────────────────────────┐
HTTP Request ──▶│  IdempotencyMiddleware                                      │
                │   1. IIdempotencyPolicy.ShouldHandle ────────────┐          │
                │   2. IIdempotencyKeyReader.TryRead ──┐            │          │
                │   3. IRequestFingerprintCalculator   │            │          │
                │   4. IIdempotencyStore.TryReserveAsync (atomic) ──┼─▶ store │
                │   5a. Acquired      → next(ctx) → CaptureAsync ─▶ store     │
                │   5b. Completed     → ReplayAsync(stored)                   │
                │   5c. InProgress    → 409                                   │
                │   5d. FpMismatch    → 422                                   │
                └────────────────────────────────────────────────────────────┘
```

Each numbered step is a swappable interface — replace the header reader with one that
extracts the key from a JWT, or replace the fingerprint with one that hashes only a
chosen subset of the body, or replace the store with Redis. The pipeline doesn't change.

## Extension points

| Interface | Default | Replace when... |
|---|---|---|
| `IIdempotencyKeyReader` | `HeaderIdempotencyKeyReader` | Key lives in JWT, query string, or composite (tenant + header). |
| `IRequestFingerprintCalculator` | `Sha256BodyFingerprintCalculator` | You need to ignore certain JSON properties, or include user/tenant in the fingerprint. |
| `IResponseRecorder` | `DefaultResponseRecorder` | You want to suppress some headers, encrypt the body at rest, or compress. |
| `IIdempotencyPolicy` | `HttpMethodIdempotencyPolicy` (default) / `EndpointAttributeIdempotencyPolicy` | Custom matching logic — e.g., header-presence-based. |
| `IIdempotencyStore` | `InMemoryIdempotencyStore` / `EfCoreIdempotencyStore<T>` | Redis, DynamoDB, Cosmos DB, ... |

Register a custom implementation **before** calling `AddAspNetCore()` and the
`TryAdd*` registrations will skip the default — your service wins.

```csharp
builder.Services.AddSingleton<IIdempotencyKeyReader, MyJwtKeyReader>();
builder.Services
    .AddIdempotentApi()
    .AddAspNetCore()         // skips HeaderIdempotencyKeyReader because TryAdd
    .UseInMemoryStore();
```

## EF Core persistence

```csharp
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> opts) : base(opts) { }

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.ApplyIdempotentApiConfigurations(schema: "idempotency");
    }
}
```

```csharp
builder.Services.AddDbContextFactory<AppDbContext>(opts =>
    opts.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services
    .AddIdempotentApi()
    .AddAspNetCore()
    .UseEntityFrameworkCore<AppDbContext>();
```

The store relies on the primary-key uniqueness of `IdempotencyRecord.Key` for atomicity:
concurrent reservations of the same key all attempt to insert; the database wins exactly
one and the loser re-reads to discover the existing entry.

## Per-endpoint opt-in (attribute policy)

```csharp
builder.Services
    .AddIdempotentApi()
    .AddAspNetCore(o => o.UseAttributePolicy = true)
    .UseInMemoryStore();
```

```csharp
app.MapPost("/orders", () => Results.Created("/orders/1", new { id = 1 }))
   .WithMetadata(new IdempotentAttribute());

[ApiController]
public class PaymentsController : ControllerBase
{
    [HttpPost("/charges")]
    [Idempotent(Expiration = "0:30:0")] // 30 min override
    public IActionResult Charge() { /* ... */ }
}
```

## Notes

- **At-least-once** delivery isn't an issue — the store guarantees at-most-once handler
  execution per key within the TTL.
- **Body buffering** is bounded by `IdempotentApiOptions.MaxBodyBytes`. Streaming uploads
  larger than this should opt out via the policy.
- **Replays** carry an `Idempotent-Replay: true` response header so clients and gateways
  can tell a fresh response from a cached one.
- **Crashes mid-handler** roll the reservation back so the next retry can claim it — the
  client doesn't get stuck waiting for an expired in-flight reservation.

## License

MIT
