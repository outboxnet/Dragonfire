# Dragonfire.TraceKit

Non-blocking API request/response tracing for ASP.NET Core B2B services.

* Captures every **inbound** API call (method, URL, headers, body, status, duration).
* Captures every **outbound third-party** HttpClient call made while serving that request.
* Preserves the **sequence** of third-party calls within a request — correct under parallel `Task.WhenAll`.
* **Storage abstracted** behind `ITraceRepository`. The library has zero dependency on EF Core, Dapper, or any specific entity.
* Customisable **redaction** for headers, JSON properties, query parameters, and regex body patterns.
* **Never blocks** the request hot path. Capture is best-effort and a slow or failing repository can never affect production traffic.

## Packages

| Package | Purpose |
| --- | --- |
| `Dragonfire.TraceKit` | Core abstractions, models, redaction, bounded-channel writer, background drain. Framework-agnostic. |
| `Dragonfire.TraceKit.AspNetCore` | Middleware + auto-attached `DelegatingHandler` for every `IHttpClientFactory` HttpClient. |

## Quick start

```csharp
// Program.cs
using Dragonfire.TraceKit.AspNetCore.Extensions;
using Dragonfire.TraceKit.Extensions;

builder.Services
    .AddTraceKitForAspNetCore(opts =>
    {
        opts.MaxBodyBytes = 32 * 1024;
        opts.IgnoredPathPrefixes = new[] { "/health", "/swagger" };
        opts.Redaction.SensitiveHeaders.Add("X-Tenant-Secret");
        opts.Redaction.SensitiveJsonProperties.Add("ssn");
    })
    .UseRepository<SqlTraceRepository>();   // your implementation

var app = builder.Build();

app.UseTraceKit();   // place early in the pipeline
app.MapControllers();
app.Run();
```

That is the entire wiring. **No HttpClient changes required** — every client created via `IHttpClientFactory` (named, typed, default) automatically gets a TraceKit handler.

## Implementing the repository

The library does not own your storage schema. Implement `ITraceRepository` against whatever storage and entity you prefer:

```csharp
using Dragonfire.TraceKit.Abstractions;
using Dragonfire.TraceKit.Models;

public sealed class SqlTraceRepository : ITraceRepository
{
    private readonly MyDbContext _db;
    public SqlTraceRepository(MyDbContext db) => _db = db;

    public async Task SaveAsync(ApiTrace trace, CancellationToken ct)
    {
        _db.ApiTraces.Add(new ApiTraceRow
        {
            TraceId        = trace.TraceId,
            CorrelationId  = trace.CorrelationId,
            Sequence       = trace.Sequence,
            Kind           = (byte)trace.Kind,
            Method         = trace.Method,
            Url            = trace.Url,
            StatusCode     = trace.StatusCode,
            DurationMs     = (int)trace.Duration.TotalMilliseconds,
            RequestBody    = trace.RequestBody,
            ResponseBody   = trace.ResponseBody,
            // ... map whichever fields you keep
        });
        await _db.SaveChangesAsync(ct);
    }
}
```

`SaveAsync` is invoked in a background hosted service from a fresh DI scope, so scoped dependencies (DbContext, etc.) work without leaks. Exceptions are caught and logged — they will never propagate into your request pipeline.

## Reconstructing the call graph

Every row carries:

* `CorrelationId` — same value for the inbound row and every outbound third-party row of one API request.
* `Sequence` — `0` for the inbound row, `1, 2, 3, …` for outbound calls in the order they began. Atomic via `Interlocked.Increment`, so the order is correct even when calls run concurrently.
* `Kind` — `Inbound` or `OutboundThirdParty`.

Listing rows for a request is just:

```sql
SELECT * FROM ApiTraces WHERE CorrelationId = @id ORDER BY Sequence;
```

## Redaction

`RedactionOptions` exposes:

* `SensitiveHeaders` — full header value replaced with `[REDACTED]`. Defaults cover `Authorization`, `Cookie`, `X-Api-Key`, etc.
* `SensitiveJsonProperties` — JSON properties whose values are rewritten regardless of value type. Defaults cover `password`, `clientSecret`, `accessToken`, `creditCard`, …
* `SensitiveQueryParameters` — query-string values replaced in URLs.
* `BodyPatterns` — list of `Regex` applied to non-JSON bodies (e.g. `Bearer …`, card numbers).
* `ReplacementToken` — the marker (`"[REDACTED]"` by default).

Add to the existing sets to extend; assign new sets to replace them entirely.

For domain-specific rules (per-tenant, policy-driven), implement `ITraceRedactor` and register it:

```csharp
builder.Services.AddTraceKitForAspNetCore().UseRedactor<MyRedactor>();
```

## Performance and reliability

* Inbound capture buffers the request body once via `Request.EnableBuffering()` and tees the response body using a write-only `TeeStream` — clients receive bytes with no extra latency.
* Outbound capture uses `HttpContent.LoadIntoBufferAsync` so the calling code can still consume the response normally.
* Captured traces are pushed to a bounded `Channel<ApiTrace>` with `BoundedChannelFullMode.DropOldest`. **The hot path never awaits storage.**
* A single `BackgroundService` drains the channel, resolves a fresh DI scope per trace, and calls `ITraceRepository.SaveAsync`. Repository exceptions are logged at `Warning` and the trace is dropped.
* All TraceKit work runs inside try/catch. A bug in tracing cannot break a request.

## Configuration reference

```jsonc
{
  "TraceKit": {
    "Enabled": true,
    "CaptureInboundBodies": true,
    "CaptureOutboundBodies": true,
    "MaxBodyBytes": 65536,
    "ChannelCapacity": 10000,
    "IgnoredPathPrefixes": [ "/health", "/metrics", "/swagger" ],
    "CapturableContentTypePrefixes": [ "application/json", "application/xml", "text/" ]
  }
}
```

Bind it manually if you prefer:

```csharp
builder.Services.Configure<TraceKitOptions>(builder.Configuration.GetSection(TraceKitOptions.SectionName));
builder.Services.AddTraceKitForAspNetCore();
```
