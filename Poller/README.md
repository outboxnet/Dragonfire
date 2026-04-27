# Poller

A production-ready .NET 8 polling framework with exponential backoff, configurable retry strategies, real-time progress streaming, and pluggable metrics.

[![NuGet](https://img.shields.io/nuget/v/Poller.svg)](https://www.nuget.org/packages/Poller)
[![CI](https://github.com/sandor/Poller/actions/workflows/ci.yml/badge.svg)](https://github.com/sandor/Poller/actions/workflows/ci.yml)

## Features

- Generic polling — any request/response type pair
- Exponential backoff with configurable multiplier and cap
- Channel-based bounded queue with configurable concurrency throttle
- Real-time progress streaming via `IAsyncEnumerable`
- Cancellation support at both the job and application level
- Automatic data cleanup based on configurable retention period
- Pluggable metrics (`NoOpMetricsTracker` by default, Azure App Insights included)
- Thread-safe design throughout (`ConcurrentDictionary`, `SemaphoreSlim`, `Channel<T>`)

---

## Installation

```bash
dotnet add package Poller
```

---

## Quick start

### 1. Define your request and response types

```csharp
public class OrderStatusRequest
{
    public string OrderId { get; set; } = "";
}

public class OrderStatusResponse
{
    public string Status { get; set; } = "";  // e.g. "PENDING", "SHIPPED", "DELIVERED"
    public string? TrackingNumber { get; set; }
}
```

### 2. Implement the polling strategy

```csharp
using Poller.Models;
using Poller.Services;

public class OrderStatusStrategy : IPollingStrategy<OrderStatusRequest, OrderStatusResponse>
{
    private readonly HttpClient _http;

    public OrderStatusStrategy(HttpClient http) => _http = http;

    public async Task<PollingResult<OrderStatusResponse>> PollAsync(
        OrderStatusRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _http.GetFromJsonAsync<OrderStatusResponse>(
                $"https://orders.example.com/api/{request.OrderId}", cancellationToken);

            return response is null
                ? PollingResult<OrderStatusResponse>.Failure("Empty response", shouldContinue: true)
                : PollingResult<OrderStatusResponse>.Complete(response);
        }
        catch (HttpRequestException ex)
        {
            // Transient error — the framework will retry with backoff
            return PollingResult<OrderStatusResponse>.Failure(ex.Message, shouldContinue: true);
        }
    }
}
```

### 3. Implement the completion condition

```csharp
using Poller.Services;

public class OrderStatusCondition : IPollingCondition<OrderStatusResponse>
{
    public bool IsComplete(OrderStatusResponse response)
        => response.Status is "DELIVERED" or "CANCELLED";

    public bool IsFailed(OrderStatusResponse response)
        => response.Status == "FAILED";
}
```

### 4. Register with DI

```csharp
// Program.cs
builder.Services.AddPollingService<OrderStatusRequest, OrderStatusResponse>(options =>
{
    options.MaxConcurrentPollings = 100;
    options.QueueCapacity         = 5_000;
    options.DataRetentionPeriod   = TimeSpan.FromHours(24);
});

// Domain implementations
builder.Services.AddScoped<IPollingStrategy<OrderStatusRequest, OrderStatusResponse>, OrderStatusStrategy>();
builder.Services.AddScoped<IPollingCondition<OrderStatusResponse>, OrderStatusCondition>();
builder.Services.AddHttpClient<OrderStatusStrategy>();
```

### 5. Use in a controller or service

```csharp
public class OrdersController : ControllerBase
{
    private readonly IPollingOrchestrator _orchestrator;

    public OrdersController(IPollingOrchestrator orchestrator)
        => _orchestrator = orchestrator;

    [HttpPost("{orderId}/track")]
    public async Task<IActionResult> TrackOrder(string orderId, CancellationToken ct)
    {
        var response = await _orchestrator.StartPollingAsync<OrderStatusRequest, OrderStatusResponse>(
            pollingType: "OrderStatus",
            request: new OrderStatusRequest { OrderId = orderId },
            options: new PollingOptions
            {
                MaxAttempts      = 20,
                InitialDelay     = TimeSpan.FromSeconds(5),
                MaxDelay         = TimeSpan.FromSeconds(60),
                BackoffMultiplier = 1.5,
                Timeout          = TimeSpan.FromMinutes(10)
            },
            cancellationToken: ct);

        return Accepted(new { pollingId = response.PollingId, statusUrl = response.StatusUrl });
    }

    [HttpGet("polling/{pollingId:guid}")]
    public async Task<IActionResult> GetStatus(Guid pollingId, CancellationToken ct)
    {
        var status = await _orchestrator.GetStatusAsync(pollingId, ct);
        return status is null ? NotFound() : Ok(status);
    }
}
```

---

## Multiple polling types

Call `AddPollingService<TRequest, TResponse>()` once per type pair. The shared infrastructure (orchestrator, registry, metrics) is registered only once.

```csharp
builder.Services
    .AddPollingService<OrderStatusRequest, OrderStatusResponse>()
    .AddPollingService<PaymentRequest, PaymentResponse>()
    .AddPollingService<WeatherPollingRequest, WeatherPollingResponse>();
```

---

## Configuration reference

| Property | Default | Description |
|---|---|---|
| `MaxConcurrentPollings` | `100` | Maximum jobs processed simultaneously |
| `QueueCapacity` | `10 000` | Maximum queued-but-unstarted jobs |
| `DefaultTimeout` | `5 min` | Fallback timeout when `PollingOptions.Timeout` is not set |
| `DefaultMaxAttempts` | `30` | Fallback max attempts when `PollingOptions.MaxAttempts` is not set |
| `DataRetentionPeriod` | `24 h` | How long completed/failed jobs are kept in memory |
| `EnableDetailedMetrics` | `true` | Controls App Insights aggregation timer |

`PollingOptions` (per-job overrides):

| Property | Default | Description |
|---|---|---|
| `MaxAttempts` | `30` | Maximum retry attempts |
| `InitialDelay` | `1 s` | Delay before the first retry |
| `MaxDelay` | `30 s` | Ceiling for exponential backoff |
| `BackoffMultiplier` | `2.0` | Multiplier applied after each failed attempt |
| `Timeout` | `5 min` | Wall-clock deadline for the entire job |
| `NotifyOnEachAttempt` | `false` | Push updates to `SubscribeToUpdatesAsync` subscribers |

---

## Real-time updates (Server-Sent Events)

```csharp
[HttpGet("polling/{pollingId:guid}/stream")]
public async Task Stream(Guid pollingId, CancellationToken ct)
{
    Response.Headers["Content-Type"] = "text/event-stream";

    await foreach (var update in _orchestrator.SubscribeToUpdatesAsync(pollingId, ct))
    {
        await Response.WriteAsync($"data: {JsonSerializer.Serialize(update)}\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }
}
```

---

## Metrics

The default `NoOpMetricsTracker` discards all telemetry. To enable **Azure Application Insights**:

```csharp
// After AddPollingService<>()
builder.Services.AddApplicationInsightsTelemetry();
builder.Services.AddApplicationInsightsPollingMetrics(); // replaces NoOp with AppInsights tracker
```

To plug in your own backend, implement `IPollingMetricsTracker` and register it:

```csharp
builder.Services.AddSingleton<IPollingMetricsTracker, MyPrometheusTracker>();
```

---

## Cancellation

```csharp
var cancelled = await _orchestrator.CancelPollingAsync(pollingId, cancellationToken);
```

---

## Sample — Weather API

`samples/Poller.Sample.WeatherApi` demonstrates the full pattern against the free [Open-Meteo](https://open-meteo.com) weather API (no API key required).

```bash
cd samples/Poller.Sample.WeatherApi
dotnet run
# Open https://localhost:5001/swagger
```

**Start a weather job:**
```http
POST /api/weather
Content-Type: application/json

{
  "latitude": 52.52,
  "longitude": 13.41,
  "locationName": "Berlin"
}
```

**Poll for result:**
```http
GET /api/weather/{pollingId}
```

**Stream updates (SSE):**
```http
GET /api/weather/{pollingId}/stream
```

---

## Publishing to NuGet

### Automatic (recommended)

1. Add `NUGET_API_KEY` to your repository's **Secrets** (`Settings → Secrets and variables → Actions`).
2. Create a GitHub Release — the `publish.yml` workflow fires automatically, packs with the release tag version, and pushes to NuGet.org.

### Manual trigger

```
Actions → Publish to NuGet → Run workflow → enter version
```

### Local pack

```bash
dotnet pack Poller/Poller.csproj -c Release -p:Version=1.0.0 -o ./nupkg
dotnet nuget push ./nupkg/Poller.1.0.0.nupkg --api-key $NUGET_API_KEY --source https://api.nuget.org/v3/index.json
```

---

## License

MIT
