# SyncLib

A small, opinionated library for running scheduled data-synchronization jobs in
.NET hosted apps. Each *provider* fetches data from somewhere (HTTP API,
queue, file system, …), maps it to your domain entities, and persists it
through a repository you control. The library handles scheduling, retries,
circuit-breaking, and observability.

## Packages

| Package | Purpose |
| --- | --- |
| `SyncLib.Abstractions` | Interfaces only — depend on this from your domain projects. |
| `SyncLib.Core` | Orchestrator, retry, circuit breaker, metrics, tracing. |
| `SyncLib.EntityFrameworkCore` | Optional EF Core implementations of `ISyncStateStore` and a generic repository. |

The entity types you sync **do not** carry sync metadata. Last-sync state
(timestamp, status, duration, error, counts) is persisted separately through
`ISyncStateStore`, so your domain model stays clean.

## Quick start

```csharp
services.AddSyncLibrary();

services.AddSyncProvider<WeatherDto, WeatherEntity>("weather")
    .WithConfiguration(new ProviderSyncConfiguration
    {
        ProviderName = "weather",
        SyncInterval = TimeSpan.FromMinutes(5)
    })
    .WithDataProvider<WeatherApiProvider>()
    .WithRepository<WeatherRepository>()
    .WithMapper<WeatherMapper>()
    .Build();

// EF-backed sync state (recommended)
services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connStr));
services.AddEntityFrameworkSyncStateStore<AppDbContext>();
```

## Hosting models

SyncLib has two entry points and you pick whichever matches your host:

| Host | Register | Drives the schedule |
| --- | --- | --- |
| ASP.NET / Worker Service | `services.AddSyncLibrary()` | A built-in `BackgroundService` runs each provider on its `SyncInterval`. |
| Azure Functions / cron job / on-demand | `services.AddSyncRunner()` | **You** invoke `ISyncRunner.RunAllAsync()` (or `RunAsync(name)`) — typically from a `[TimerTrigger]`. |

The same fetch → map → repository → state → metrics pipeline runs in both
modes. Your `WithRepository<TYourRepo>()` is what actually persists data, so
"the common logic" is shared regardless of who pulls the trigger.

### Azure Functions example

```csharp
// Program.cs
builder.Services.AddSyncRunner();
builder.Services.AddSyncProvider<WeatherDto, WeatherEntity>("weather")
    .WithConfiguration(new ProviderSyncConfiguration { ProviderName = "weather", MaxRetryAttempts = 2 })
    .WithDataProvider<WeatherApiProvider>()
    .WithRepository<MyWeatherRepository>()   // your own implementation
    .WithMapper<WeatherMapper>()
    .Build();

// SyncTimerFunction.cs
[Function("RunAllSyncs")]
public Task Run([TimerTrigger("0 */5 * * * *")] TimerInfo timer, CancellationToken ct)
    => _runner.RunAllAsync(ct);
```

Full sample under `samples/SyncLib.Sample.AzureFunctions`.

## Observability

* **Sync state per provider** — query `ISyncStateReader` for last run time,
  status, duration, record count, and last error message.
* **Metrics** — `synclib.sync.duration`, `synclib.sync.records`,
  `synclib.sync.failures`, `synclib.sync.successes` exposed on the
  `SyncLib` Meter.
* **Tracing** — `SyncLib` `ActivitySource` emits a span per run with
  `sync.provider`, `sync.records`, and `sync.status` tags.

Sample app under `samples/SyncLib.Sample.WebApi` exposes a `/sync/state`
endpoint that returns the last-sync record for every provider.

## License

MIT
