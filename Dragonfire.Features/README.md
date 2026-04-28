# Dragonfire.Features

Feature toggle and release-gate library for .NET 8 — built for B2B2C platforms that
need different tiers, early-access cohorts, and gradual rollouts without a roundtrip
to a SaaS flag service.

## Packages

| Package | Purpose |
|---------|---------|
| `Dragonfire.Features` | Core: `[FeatureGate]`, `IFeatureResolver`, `IFeatureSource`, in-memory store, periodic refresh hosted service, audit log abstraction |
| `Dragonfire.Features.AspNetCore` | Action filter, endpoint filter, `RequireFeature("...")`, HttpContext-aware accessor |
| `Dragonfire.Features.Configuration` | `IConfiguration` source — appsettings, env, Azure App Configuration |
| `Dragonfire.Features.EntityFrameworkCore` | EF Core source + audit log persistence (any provider) |
| `Dragonfire.Features.Caching` | `Dragonfire.Caching` decorator that caches decisions per (feature, tenant, user) |

## Quick start

```csharp
builder.Services.AddDragonfireFeatures(o =>
{
    o.RefreshInterval = TimeSpan.FromSeconds(30);
});
builder.Services.AddDragonfireFeaturesConfiguration(builder.Configuration);
builder.Services.AddDragonfireFeaturesAspNetCore();
```

```jsonc
// appsettings.json
{
  "Features": {
    "NewCheckout": {
      "DefaultEnabled": false,
      "Tenants": [ "acme", "globex" ],
      "Percentage": 25,
      "PercentageBucket": "TenantThenUser"
    }
  }
}
```

```csharp
[FeatureGate("NewCheckout")]
public class CheckoutController : ControllerBase { ... }

app.MapPost("/orders", CreateOrder).RequireFeature("NewCheckout");
```

## Rule semantics

Rules are evaluated in order; the first non-null verdict wins. If every rule
abstains, `FeatureDefinition.DefaultEnabled` is the final answer.

- **`TenantAllowListRule`** — grants when the current tenant id is in the list, otherwise abstains.
- **`UserAllowListRule`** — grants when the current user id is in the list, otherwise abstains.
- **`PercentageRule`** — stable FNV-1a hash of `(featureName, bucketKey)` mod 100; grants when the bucket is below the threshold. Bucket key prefers tenant id, then user id; anonymous calls abstain so they don't randomly flip on every request.

## Sources

`IFeatureSource.LoadAllAsync()` is called every refresh tick. When two sources advertise
the same feature name, the source registered later in DI wins — register configuration
first, EF Core second to let the database override appsettings.

The refresh service uses `PeriodicTimer` directly rather than the `Dragonfire.Poller`
library: Poller targets request/response polling with retry/backoff, not "tick every N
seconds forever". Refreshing a feature snapshot is the latter.

## Audit log

Every refresh that produces a non-empty diff calls `IFeatureAuditLog.RecordAsync`
with one entry per added / updated / removed feature. The default
`NoOpFeatureAuditLog` discards entries; the EF Core integration persists them to
`features.FeatureAudit` for B2B compliance.

## Caching integration

`AddDragonfireFeaturesCaching()` decorates the resolver. Each decision is cached as
`features:{feature}:{tenantOrNone}:{userOrNone}` with tags
`features:{feature}` and `features-tenant:{tenantId}`. Flush a single tenant's slice
with `cacheService.InvalidateByTagAsync("features-tenant:acme")`.

## TenantContext

The AspNetCore accessor reads tenant id from a configurable header
(default `X-Tenant-Id`) or claim. Users on `Dragonfire.TenantContext` can swap in
their own `IFeatureContextAccessor` that reads `ITenantContextAccessor.Current` —
`Dragonfire.Features` deliberately avoids a hard dependency on TenantContext.
