# Dragonfire.Caching

Composable, production-ready caching for .NET 8. Start with an in-memory provider and swap in Redis, a hybrid tier, distributed tag invalidation, or Protobuf serialization — each as a separate NuGet package with no mandatory dependencies on things you don't use.

---

## The problem this solves

In large codebases, caching tends to rot. It starts as a few `_memoryCache.TryGetValue` calls scattered across services. Over time you get:

- **Stale data bugs** — a write somewhere doesn't know which cache keys to evict, so users see old data until TTL expires.
- **Cache stampedes** — 200 requests all miss at the same time, all hit the database simultaneously, all try to write the same key back.
- **Untestable services** — caching logic is baked into service methods. You can't test the business logic without also testing the cache.
- **Scattered key strings** — `"user:42"` is typed as a raw string in a dozen places. One typo, one missed update, and invalidation breaks silently.
- **Provider lock-in** — you chose `IMemoryCache` on day one. Now you need Redis. Everything has to change.
- **No observability** — you have no idea what your hit rate is, which operations are cold, or how large the cache has grown.

Dragonfire.Caching addresses all of these:

| Problem | Solution |
|---------|---------|
| Stale data | Tag-based invalidation — one `InvalidateByTagAsync("user:42")` evicts every related key |
| Cache stampedes | `CacheLockManager` — per-key `SemaphoreSlim` prevents concurrent factory execution |
| Untestable services | `ICacheService` / `ICacheProvider` are interfaces — mock them freely |
| Scattered key strings | `[Cache(KeyTemplate = "user:{id}")]` or fluent builder — key format lives next to the method |
| Provider lock-in | Swap providers by changing one `AddDragonfire*` call in `Program.cs` |
| No observability | `CacheStatistics` per provider + OpenTelemetry counters via `System.Diagnostics.Metrics` |

---

## Packages

Install only what you need. Every package targets **net8.0**.

```
Dragonfire.Caching                         ← always required (core)
Dragonfire.Caching.Memory                  ← in-process IMemoryCache provider
Dragonfire.Caching.Distributed             ← any IDistributedCache backend
Dragonfire.Caching.Hybrid                  ← L1 memory + L2 distributed, auto-promote
Dragonfire.Caching.Redis                   ← Redis-backed tag index (distributed invalidation)
Dragonfire.Caching.Serialization.Protobuf  ← swap JSON for Protobuf
Dragonfire.Caching.Generator               ← compile-time proxy generator (replaces DispatchProxy)
Dragonfire.Caching.Grpc                    ← cache-aside interceptors for gRPC client + server
```

```
dotnet add package Dragonfire.Caching
dotnet add package Dragonfire.Caching.Memory
```

---

## Quick start

### In-memory (single node)

```csharp
// Program.cs
builder.Services.AddDragonfireMemoryCache(
    configureMemory: o => o.SizeLimit = 50_000);
```

```csharp
// Anywhere in your app
public class OrderService(ICacheService cache)
{
    public Task<Order?> GetAsync(int id) =>
        cache.GetOrAddAsync(
            key: $"order:{id}",
            factory: () => _db.Orders.FindAsync(id).AsTask(),
            configureOptions: o => o.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5));
}
```

### Redis-backed distributed cache

```csharp
// Register StackExchange.Redis first (your choice of package)
builder.Services.AddStackExchangeRedisCache(o => o.Configuration = "localhost:6379");

// Then the provider
builder.Services.AddDragonfireDistributedCache();
```

### Hybrid — L1 memory + L2 Redis

Reads hit L1 first. On L1 miss the value is fetched from Redis and promoted to L1. Writes go to both tiers in parallel.

```csharp
builder.Services.AddStackExchangeRedisCache(o => o.Configuration = "localhost:6379");

builder.Services.AddDragonfireHybridCache(
    configureMemory: o => o.SizeLimit = 10_000);
```

---

## Configuration-driven caching (`CacheExecutor`)

For large teams, keeping TTLs and tag policies out of C# attributes and in configuration means you can tune caching without a redeploy.

### appsettings.json

```json
{
  "Caching": {
    "DefaultTtlSeconds": 300,
    "Operations": {
      "Order.GetById": {
        "TtlSeconds": 600,
        "Tags": ["order:{Id}"]
      },
      "Order.GetByUser": {
        "TtlSeconds": 120,
        "Tags": ["user:{UserId}:orders"]
      },
      "Order.Update": {
        "InvalidatesTags": ["order:{Id}", "user:{UserId}:orders"]
      }
    }
  }
}
```

### Program.cs

```csharp
builder.Services
    .AddDragonfireMemoryCache()
    .AddDragonfireCaching(builder.Configuration, configure: b =>
        b.UseQueuedInvalidation(o =>
        {
            o.Capacity      = 10_000;  // bounded queue
            o.DropWhenFull  = false;   // back-pressure (default)
            o.ConsumerCount = 2;       // parallel invalidation workers
        }));
```

### Define operations (once, as constants)

```csharp
public static class OrderOps
{
    public static readonly CacheOperation GetById   = new("Order.GetById");
    public static readonly CacheOperation GetByUser = new("Order.GetByUser");
    public static readonly CacheOperation Update    = new("Order.Update");
}
```

### Use in a service

```csharp
public class OrderService(CacheExecutor cache)
{
    // Cache result. Tags registered from config ("order:99").
    public Task<Order> GetByIdAsync(int id) =>
        cache.GetOrCreateAsync(
            OrderOps.GetById,
            parameters: new { Id = id },
            factory: () => _db.FindAsync(id));

    // Execute action, then enqueue tag invalidation asynchronously.
    public Task UpdateAsync(int id, int userId, OrderDto dto) =>
        cache.ExecuteAndInvalidateAsync(
            OrderOps.Update,
            parameters: new { Id = id, UserId = userId },
            action: () => _db.UpdateAsync(id, dto));
}
```

The `InvalidationWorker` background service processes the queue and evicts all keys tagged `order:99` and `user:7:orders` — without the service needing to know what those keys are.

---

## Attribute-based caching (decorator pattern)

Register your service with the proxy decorator. No base class, no interface changes needed.

```csharp
// Program.cs
builder.Services.AddDragonFireCachedService<IOrderService, OrderService>();
```

```csharp
public class OrderService : IOrderService
{
    // Cache with a template — only {id} in the key.
    [Cache(KeyTemplate = "order:{id}", AbsoluteExpirationSeconds = 600)]
    public virtual Task<Order?> GetByIdAsync(int id) =>
        _db.Orders.FindAsync(id).AsTask();

    // Use tags for group invalidation.
    [Cache(
        KeyTemplate = "user:{userId}:orders:{status}",
        SlidingExpirationSeconds = 120,
        Tags = ["user:{userId}:orders"])]
    public virtual Task<List<Order>> GetByUserAsync(int userId, OrderStatus status) =>
        _db.Orders.Where(o => o.UserId == userId && o.Status == status).ToListAsync();

    // Evict by key pattern and tag after the write completes.
    [CacheInvalidate("order:{id}:*")]
    [CacheInvalidate(Tag = "user:{userId}:orders")]
    public virtual Task UpdateAsync(int id, int userId, OrderDto dto) =>
        _db.UpdateAsync(id, dto);
}
```

> **Important:** Methods must be `virtual` (or on an interface) for the proxy to intercept them.

---

## Multiple parameters — which go into the key?

You have four options, from most explicit to least.

### Option 1 — Template (recommended)

Name exactly the placeholders you want. Everything else is ignored.

```csharp
// Method: GetOrdersAsync(int userId, OrderStatus status, bool includeDeleted, string sortBy)
// Key:    "orders:42:Active"  — includeDeleted and sortBy are irrelevant to the cache result

[Cache(KeyTemplate = "orders:{userId}:{status}", AbsoluteExpirationSeconds = 300)]
public virtual Task<List<Order>> GetOrdersAsync(
    int userId, OrderStatus status, bool includeDeleted, string sortBy) { ... }
```

You can also use positional placeholders (`{0}`, `{1}`, ...):

```csharp
[Cache(KeyTemplate = "orders:{0}:{1}", AbsoluteExpirationSeconds = 300)]
public virtual Task<List<Order>> GetOrdersAsync(int userId, OrderStatus status, ...) { ... }
```

### Option 2 — `[CacheKey]` attribute (selective auto-generation)

Mark the parameters that matter. Only marked ones appear in the auto-generated key. Untagged ones (including `CancellationToken`, audit flags, etc.) are excluded.

```csharp
[Cache(AbsoluteExpirationSeconds = 300)]
public virtual Task<List<Order>> GetOrdersAsync(
    [CacheKey] int userId,           // ✅ in key
    [CacheKey] OrderStatus status,   // ✅ in key
    bool includeDeleted,             // ❌ excluded (another param has [CacheKey])
    CancellationToken ct)            // ❌ excluded
// → key: "OrderService.GetOrdersAsync(userId=42,status=Active)"
```

You can also rename a parameter in the key:

```csharp
[Cache(AbsoluteExpirationSeconds = 300)]
public virtual Task<Order?> GetByIdAsync([CacheKey("id")] int orderId, string expand)
// → key: "OrderService.GetByIdAsync(id=99)"
```

### Option 3 — All parameters (no template, no `[CacheKey]`)

Safe when all parameters are value types or strings. Complex objects fall back to `GetHashCode()` which is not stable across restarts — use Option 1 or 2 for those.

```csharp
[Cache(AbsoluteExpirationSeconds = 300)]
public virtual Task<Report> GenerateAsync(int year, int month, ReportType type)
// → key: "ReportService.GenerateAsync(year=2024,month=3,type=Monthly)"
```

### Option 4 — Fluent builder (no attributes at all)

```csharp
builder.Services.AddDragonFireCachedService<IOrderService, OrderService>(b => b
    .Cache(
        s => s.GetOrdersAsync(default, default, default, default),
        cacheKeyTemplate: "orders:{userId}:{status}",   // only these two in key
        expiration: TimeSpan.FromMinutes(5))
    .Invalidate(
        s => s.UpdateOrderAsync(default, default),
        cacheKeyTemplate: "orders:{userId}:*"));        // glob wipes all statuses
```

### Summary

| Approach | Which params go in the key |
|----------|---------------------------|
| `KeyTemplate = "x:{a}:{b}"` | Only the placeholders you list |
| `[CacheKey]` on some params | Only the marked params |
| `[CacheKey]` on no params | All params (same as next row) |
| No template, no `[CacheKey]` | All params (value types / strings — safe) |
| Manual `ICacheService` | Whatever string you build yourself |

---

## Tag-based invalidation

Tags let you evict a group of related keys with a single call, regardless of how many variations are cached.

```csharp
// Store with tags
await cache.GetOrAddWithTagsAsync(
    key: $"user:{userId}:profile",
    factory: () => _db.GetProfileAsync(userId),
    tags: [$"user:{userId}"],
    configureOptions: o => o.SlidingExpiration = TimeSpan.FromMinutes(10));

await cache.GetOrAddWithTagsAsync(
    key: $"user:{userId}:orders:pending",
    factory: () => _db.GetOrdersAsync(userId, OrderStatus.Pending),
    tags: [$"user:{userId}", "orders:pending"]);

// One call evicts ALL keys tagged "user:42" — profile and orders both gone
await cache.InvalidateByTagAsync($"user:{userId}");
```

For distributed scenarios (multiple nodes), replace the default in-memory tag index with Redis:

```csharp
// Option A — from the caching builder
builder.Services.AddDragonfireCaching(b => b.UseRedisTagIndex());

// Option B — standalone registration (if IConnectionMultiplexer is already registered)
builder.Services.AddDragonfireRedisTagIndex();

// Option C — register multiplexer and tag index together
builder.Services.AddDragonfireRedisTagIndex("localhost:6379");
```

---

## Invalidation queue parameters

The channel-based invalidation queue can be tuned for high-throughput scenarios.

```csharp
builder.Services.AddDragonfireCaching(builder.Configuration, configure: b =>
    b.UseQueuedInvalidation(o =>
    {
        // null = unbounded (default). Set a number to apply back-pressure.
        o.Capacity = 50_000;

        // When at capacity:
        //   false (default) = wait for space (back-pressure, no data loss)
        //   true            = silently drop the oldest item (lossy, never blocks)
        o.DropWhenFull = false;

        // Number of parallel workers draining the queue.
        // Increase if invalidation throughput is a bottleneck.
        o.ConsumerCount = 4;
    }));
```

---

## Direct invalidation (without the queue)

If you need synchronous, guaranteed invalidation before returning to the caller:

```csharp
// By exact key
await cache.RemoveAsync("order:99");

// By glob pattern (all variants of an order)
await cache.RemoveByPatternAsync("order:99:*");

// By tag (all keys associated with this tag)
await cache.InvalidateByTagAsync("user:42");
```

> **Note:** `RemoveByPatternAsync` on the `DistributedCacheProvider` uses an in-process key index — it works correctly when all writes go through the same provider instance. For multi-node deployments, use tag-based invalidation with `RedisTagIndex` instead.

---

## Distributed locking (stampede prevention)

```csharp
// The factory runs at most once per key, even under concurrent load.
// Other callers wait until the first one populates the cache.
var result = await cache.GetOrAddWithLockAsync(
    key: $"expensive-report:{date}",
    factory: () => _reportEngine.GenerateAsync(date),
    configureOptions: o => o.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1),
    lockTimeout: TimeSpan.FromSeconds(30));
```

`GetOrAddWithLockAsync` uses `CacheLockManager` — per-key `SemaphoreSlim` locks, **in-process only**. For distributed locking across multiple nodes, use a library like [RedLock.net](https://github.com/samcook/RedLock.net) and call `ICacheService.GetOrAddAsync` inside your own lock.

---

## Custom serializer

### Protobuf (for generated `IMessage` types)

```
dotnet add package Dragonfire.Caching.Serialization.Protobuf
```

```csharp
builder.Services.AddDragonfireDistributedCache(configure: b =>
    b.UseProtobufSerializer());
```

### Custom JSON options

```csharp
builder.Services.AddDragonfireMemoryCache(configureCaching: b =>
    b.UseJsonSerializer(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter() }
    }));
```

### Bring your own serializer

Implement `ICacheSerializer` and register it:

```csharp
public class MessagePackCacheSerializer : ICacheSerializer
{
    public byte[] Serialize<T>(T value) => MessagePackSerializer.Serialize(value);
    public T Deserialize<T>(byte[] bytes) => MessagePackSerializer.Deserialize<T>(bytes);
}

builder.Services.AddDragonfireMemoryCache(configureCaching: b =>
    b.UseSerializer<MessagePackCacheSerializer>());
```

---

## Custom key strategy

By default, auto-generated keys look like `OrderService.GetByIdAsync(id=99)`. Replace the strategy globally:

```csharp
public class PrefixedKeyStrategy : DefaultCacheKeyStrategy
{
    private readonly string _prefix;
    public PrefixedKeyStrategy(string prefix) => _prefix = prefix;

    public override string GenerateKey(MethodInfo method, object?[] arguments, string? keyTemplate = null)
        => $"{_prefix}:{base.GenerateKey(method, arguments, keyTemplate)}";
}

builder.Services.AddSingleton<ICacheKeyStrategy>(new PrefixedKeyStrategy("myapp"));
```

---

## Custom tag index

```csharp
// Replace the default in-memory tag index with any implementation:
builder.Services.AddDragonfireCaching(b =>
    b.UseTagIndex<MyCustomTagIndex>());
```

---

## Custom cache provider

Implement `ICacheProvider` and register it:

```csharp
public class DynamoDbCacheProvider : ICacheProvider { ... }

builder.Services
    .AddDragonfireCaching()
    .AddDragonfireCacheProvider<DynamoDbCacheProvider>();
```

---

## Expiration options

```csharp
// Absolute — expires at a fixed point in time
o.AbsoluteExpiration = DateTimeOffset.UtcNow.AddHours(2);

// Absolute relative to now — most common
o.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);

// Sliding — expiration resets on every cache hit
o.SlidingExpiration = TimeSpan.FromMinutes(10);

// Convenience factory methods
var opts = CacheEntryOptions.Absolute(TimeSpan.FromMinutes(5));
var opts = CacheEntryOptions.Sliding(TimeSpan.FromMinutes(10));
var opts = CacheEntryOptions.NeverExpire;     // Priority = NeverRemove (memory only)

// Tags + expiration together
await cache.SetAsync("key", value, o =>
{
    o.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
    o.Tags.Add("user:42");
    o.Priority = CacheItemPriority.High;      // memory provider only
});
```

---

## Bulk operations

```csharp
// Read many keys at once
IDictionary<string, User?> users = await provider.GetMultipleAsync<User>(
    ["user:1", "user:2", "user:3"]);

// Write many keys at once
await provider.SetMultipleAsync(
    new Dictionary<string, User>
    {
        ["user:1"] = user1,
        ["user:2"] = user2,
    },
    CacheEntryOptions.Absolute(TimeSpan.FromMinutes(10)));
```

---

## Observability

### Cache statistics

```csharp
public class CacheDiagnosticsController(ICacheService cache) : ControllerBase
{
    [HttpGet("cache/stats")]
    public IActionResult Stats()
    {
        var s = cache.GetStatistics();
        return Ok(new
        {
            provider   = cache.ProviderName,
            hitRatio   = s.HitRatio.ToString("P1"),   // "94.3%"
            hits       = s.TotalHits,
            misses     = s.TotalMisses,
            sets       = s.TotalSets,
            removals   = s.TotalRemovals,
            entryCount = s.CurrentEntryCount,
            sinceUtc   = s.LastReset
        });
    }
}
```

### OpenTelemetry metrics

The library records four counters under the `Dragonfire.Caching` meter:

| Metric name | Tags | Description |
|-------------|------|-------------|
| `dragonfire.cache.hits` | `provider` | Total cache hits |
| `dragonfire.cache.misses` | `provider` | Total cache misses |
| `dragonfire.cache.sets` | `provider` | Total cache sets |
| `dragonfire.cache.removals` | `provider` | Total cache removals |

```csharp
// Prometheus via OpenTelemetry
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m
        .AddMeter("Dragonfire.Caching")
        .AddPrometheusExporter());
```

---

## Caching gRPC calls

`Dragonfire.Caching` integrates with gRPC two different ways. Pick whichever fits your codebase — they are not mutually exclusive.

### Option B — wrap the proto client behind your own interface (zero new packages)

The compile-time generator (`Dragonfire.Caching.Generator`) only needs an interface that implements `ICacheable`. Because proto-generated clients are sealed and you can't add `[Cache]` to them, you write a thin wrapper that *is* attribute-decorated, and let the generator produce the proxy:

```csharp
using Dragonfire.Caching.Abstractions;
using Dragonfire.Caching.Attributes;
using Order;   // proto-generated namespace

public interface IOrderClient : ICacheable
{
    [Cache(SlidingExpirationSeconds = 300, KeyTemplate = "order:{tenantId}:{orderId}",
           Tags = new[] { "tenant:{tenantId}" })]
    Task<OrderReply> GetOrderAsync(string tenantId, string orderId);

    [CacheInvalidate("order:{tenantId}:*")]
    Task UpdateOrderAsync(string tenantId, OrderReply order);
}

public sealed class OrderClient(OrderService.OrderServiceClient inner)
    : IOrderClient, ICacheable
{
    public Task<OrderReply> GetOrderAsync(string tenantId, string orderId) =>
        inner.GetOrderAsync(new GetOrderRequest { TenantId = tenantId, OrderId = orderId })
             .ResponseAsync;

    public Task UpdateOrderAsync(string tenantId, OrderReply order) =>
        inner.UpdateOrderAsync(new UpdateOrderRequest { TenantId = tenantId, Order = order })
             .ResponseAsync;
}
```

Registration is unchanged from any other `ICacheable`:

```csharp
builder.Services.AddGrpcClient<OrderService.OrderServiceClient>(o =>
    o.Address = new Uri("https://order-service:5001"));
builder.Services.AddScoped<IOrderClient, OrderClient>();

builder.Services.AddDragonfireMemoryCache().AddDragonfireCaching();
builder.Services.AddDragonfireGeneratedCaching();   // wraps OrderClient automatically
```

Use this when:
- You already have a domain layer and the gRPC client is just one of several backends.
- You want full attribute-driven semantics (templates, tags, multiple `[CacheInvalidate]`).
- You don't want a runtime gRPC interceptor in the call path.

### Option D — `Dragonfire.Caching.Grpc` interceptors (no wrapper required)

When you can't or won't write a wrapper — for example you call the proto client directly throughout your code — install `Dragonfire.Caching.Grpc`. It ships two `Grpc.Core.Interceptors.Interceptor` subclasses that read scalar fields from the proto request via descriptor reflection (same approach as `Dragonfire.Logging.Grpc`) and apply cache-aside / invalidation rules registered in DI.

**Client side — cache outbound unary calls:**

```csharp
using Dragonfire.Caching.Grpc.Configuration;
using Dragonfire.Caching.Grpc.Extensions;
using Dragonfire.Caching.Grpc.Interceptors;

builder.Services.AddDragonfireMemoryCache().AddDragonfireCaching();

builder.Services.AddDragonfireGrpcClientCaching(options =>
{
    options.Cache(new GrpcCacheRule
    {
        FullMethod        = "/order.OrderService/GetOrder",
        KeyTemplate       = "order:{tenantId}:{orderId}",
        SlidingExpiration = TimeSpan.FromMinutes(5),
        Tags              = new[] { "tenant:{tenantId}" }
    });

    options.Invalidate(new GrpcInvalidateRule
    {
        FullMethod  = "/order.OrderService/UpdateOrder",
        KeyPatterns = new[] { "order:{tenantId}:*" },
        Tags        = new[] { "tenant:{tenantId}" }
    });
});

builder.Services.AddGrpcClient<OrderService.OrderServiceClient>(o =>
        o.Address = new Uri("https://order-service:5001"))
    .AddInterceptor<DragonfireClientCachingInterceptor>();
```

**Server side — short-circuit inbound unary handlers:**

```csharp
builder.Services.AddDragonfireGrpcServerCaching(options =>
{
    options.Cache(new GrpcCacheRule
    {
        FullMethod        = "/order.OrderService/GetOrder",
        KeyTemplate       = "order:{tenantId}:{orderId}",
        SlidingExpiration = TimeSpan.FromMinutes(5)
    });
});

builder.Services.AddGrpc(o =>
    o.Interceptors.Add<DragonfireServerCachingInterceptor>());
```

Behaviour:
- **Unary calls only.** Streaming (client-streaming, server-streaming, bidi) passes through untouched — caching streamed payloads is not safe in a generic way.
- **Cache keys** are built by the registered `ICacheKeyStrategy`. Templates use proto JSON field names (lowerCamelCase), e.g. `{tenantId}` for proto field `tenant_id`. With no template, the auto-key is `Service.Method(field=value,...)`.
- **`IncludeFields`** narrows which scalar fields participate in the key — useful when the request carries a correlation ID or auth token that would make every key unique.
- **`Tags`** templates are also resolved from the request, so a single tag like `"tenant:{tenantId}"` flushes everything for one tenant.

Use this when:
- You don't own the call sites (libraries, generated clients passed around).
- You want server-side caching for read-heavy RPCs without changing handler code.
- You want consistent rules driven from configuration rather than attributes.

---

## Migration guide — from `IMemoryCache` / `IDistributedCache`

**Before:**
```csharp
if (!_cache.TryGetValue($"user:{id}", out User user))
{
    user = await _db.Users.FindAsync(id);
    _cache.Set($"user:{id}", user, TimeSpan.FromMinutes(5));
}
return user;
```

**After:**
```csharp
return await _cache.GetOrAddAsync(
    $"user:{id}",
    () => _db.Users.FindAsync(id).AsTask(),
    o => o.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5));
```

To swap providers later, change **only** `Program.cs` — nothing in your services changes.

---

## Architecture overview

```
┌─────────────────────────────────────────────────────────────┐
│  Your application                                           │
│                                                             │
│  ICacheService ──────────────────────────────────────────┐  │
│       │                                                  │  │
│  CacheExecutor (config-driven, with queued invalidation) │  │
│       │                                                  │  │
│  CachingProxy<T> (attribute or fluent decorator)         │  │
└───────┼──────────────────────────────────────────────────┼──┘
        │                                                  │
        ▼                                                  ▼
  ICacheProvider                                      ITagIndex
  ├─ MemoryCacheProvider     (Dragonfire.Caching.Memory)
  ├─ DistributedCacheProvider (Dragonfire.Caching.Distributed)
  └─ HybridCacheProvider     (Dragonfire.Caching.Hybrid)
        │                         ├─ InMemoryTagIndex  (core)
        ▼                         └─ RedisTagIndex     (Dragonfire.Caching.Redis)
  ICacheSerializer
  ├─ SystemTextJsonSerializer (core, default)
  └─ ProtobufSerializer       (Dragonfire.Caching.Serialization.Protobuf)

  IInvalidationQueue ──► InvalidationWorker (BackgroundService)
  └─ ChannelInvalidationQueue (configurable capacity + consumers)
```

---

## Full example

```csharp
// Program.cs — hybrid provider, Redis tags, queued invalidation
builder.Services
    .AddStackExchangeRedisCache(o => o.Configuration = "localhost:6379")
    .AddDragonfireHybridCache(
        configureMemory: o => o.SizeLimit = 10_000,
        configureCaching: b => b
            .UseQueuedInvalidation(o => { o.Capacity = 20_000; o.ConsumerCount = 2; })
            .UseRedisTagIndex())
    .AddDragonfireCaching(builder.Configuration);   // binds CacheSettings from appsettings

// Register a service with the proxy decorator
builder.Services.AddDragonFireCachedService<IOrderService, OrderService>();
```

```csharp
// OrderService.cs
public class OrderService : IOrderService
{
    private readonly AppDbContext _db;
    public OrderService(AppDbContext db) => _db = db;

    [Cache(
        KeyTemplate = "order:{id}",
        AbsoluteExpirationSeconds = 600,
        Tags = ["order:{id}", "user:{userId}:orders"])]
    public virtual Task<Order?> GetByIdAsync(int id, int userId) =>
        _db.Orders.FindAsync(id).AsTask();

    [Cache(
        KeyTemplate = "user:{userId}:orders:{status}",
        SlidingExpirationSeconds = 120,
        Tags = ["user:{userId}:orders"])]
    public virtual Task<List<Order>> GetByUserAsync(
        [CacheKey] int userId,
        [CacheKey] OrderStatus status,
        bool includeArchived,           // not in cache key
        CancellationToken ct = default) =>
        _db.Orders
            .Where(o => o.UserId == userId && o.Status == status)
            .ToListAsync(ct);

    [CacheInvalidate("order:{id}:*")]
    [CacheInvalidate(Tag = "user:{userId}:orders")]
    public virtual async Task UpdateAsync(int id, int userId, OrderDto dto)
    {
        await _db.UpdateOrderAsync(id, dto);
    }
}
```
