using Microsoft.EntityFrameworkCore;
using SyncLib.Abstractions;
using SyncLib.Core.DependencyInjection;
using SyncLib.EntityFrameworkCore;
using SyncLib.Sample.WebApi;
using SyncLib.Sample.WebApi.Sync;

var builder = WebApplication.CreateBuilder(args);

// Database — use SQL Server when "ConnectionStrings:Default" is set, otherwise
// fall back to InMemory so the sample runs with no infrastructure.
var connectionString = builder.Configuration.GetConnectionString("Default");
builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        options.UseInMemoryDatabase("synclib-sample");
    }
    else
    {
        options.UseSqlServer(connectionString);
    }
});

// SyncLib core + EF Core state store.
builder.Services.AddSyncLibrary();
builder.Services.AddEntityFrameworkSyncStateStore<AppDbContext>();

// Single API client backs every weather stream. The consumer owns this
// registration — SyncLib never assumes how the client is constructed (HTTP,
// gRPC, in-proc fake, etc.).
builder.Services.AddScoped<IWeatherApi, FakeWeatherApi>();

// Stream 1: forecasts — runs every 30 seconds.
builder.Services.AddSyncStream<IWeatherApi, ForecastDto>("weather", "forecasts")
    .WithFetch((api, since, ct) => api.GetForecastsAsync(since, ct))
    .HandledBy<ForecastHandler>()
    .WithSchedule(TimeSpan.FromSeconds(30))
    .Build();

// Stream 2: alerts — runs every 10 seconds, against the SAME client.
builder.Services.AddSyncStream<IWeatherApi, AlertDto>("weather", "alerts")
    .WithFetch((api, since, ct) => api.GetAlertsAsync(since, ct))
    .HandledBy<AlertHandler>()
    .WithSchedule(TimeSpan.FromSeconds(10))
    .Build();

var app = builder.Build();

// Ensure schema (sample only — production should use migrations).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.IsRelational())
    {
        db.Database.Migrate();
    }
    else
    {
        db.Database.EnsureCreated();
    }
}

// ---- Observability endpoints ---------------------------------------------

// All streams' last sync state.
app.MapGet("/sync/state", async (ISyncStateReader reader, CancellationToken ct) =>
    Results.Ok(await reader.GetAllAsync(ct)));

// One provider's streams.
app.MapGet("/sync/state/{providerName}", async (string providerName, ISyncStateReader reader, CancellationToken ct) =>
    Results.Ok(await reader.GetByProviderAsync(providerName, ct)));

// One stream.
app.MapGet("/sync/state/{providerName}/{streamName}", async (string providerName, string streamName, ISyncStateReader reader, CancellationToken ct) =>
    await reader.GetAsync(new SyncStateKey(providerName, streamName), ct) is { } state
        ? Results.Ok(state)
        : Results.NotFound());

// Trigger a manual sync now for one stream.
app.MapPost("/sync/{providerName}/{streamName}/run", async (string providerName, string streamName, SyncLib.Core.ISyncOrchestrator orch, CancellationToken ct) =>
{
    await orch.TriggerManualSyncAsync(new SyncStateKey(providerName, streamName), ct);
    return Results.Accepted();
});

// Cheap health probe based on sync state.
app.MapGet("/healthz/sync", async (ISyncStateReader reader, CancellationToken ct) =>
{
    var states = await reader.GetAllAsync(ct);
    var unhealthy = states.Where(s => s.LastStatus is SyncStatus.Failed or SyncStatus.Skipped).ToArray();
    return unhealthy.Length == 0
        ? Results.Ok(new { status = "healthy", streams = states.Count })
        : Results.Json(new { status = "degraded", failing = unhealthy.Select(s => s.Key.ToString()) }, statusCode: 503);
});

// Dump persisted rows to demonstrate the sync ran end-to-end.
app.MapGet("/forecasts", async (AppDbContext db, CancellationToken ct) =>
    Results.Ok(await db.Forecasts.AsNoTracking().OrderBy(w => w.City).ToListAsync(ct)));

app.MapGet("/alerts", async (AppDbContext db, CancellationToken ct) =>
    Results.Ok(await db.Alerts.AsNoTracking().OrderByDescending(a => a.IssuedAt).Take(50).ToListAsync(ct)));

app.MapGet("/", () => Results.Text("""
    SyncLib sample.
      GET  /sync/state                          — last sync state for all streams
      GET  /sync/state/{provider}               — streams for one provider
      GET  /sync/state/{provider}/{stream}      — one stream
      POST /sync/{provider}/{stream}/run        — trigger a sync now
      GET  /forecasts                           — synced forecasts
      GET  /alerts                              — synced alerts
      GET  /healthz/sync                        — degraded if any stream failed/skipped last
    """));

app.Run();
