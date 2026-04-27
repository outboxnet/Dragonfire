using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SyncLib.Core.DependencyInjection;
using SyncLib.Sample.AzureFunctions.Sync;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// Functions: register the runner ONLY (no hosted scheduler — the timer trigger
// is the scheduler).
builder.Services.AddSyncRunner();

// Single API client backs every weather stream.
builder.Services.AddScoped<IWeatherApi, FakeWeatherApi>();

builder.Services.AddSyncStream<IWeatherApi, ForecastDto>("weather", "forecasts")
    .WithFetch((api, since, ct) => api.GetForecastsAsync(since, ct))
    .HandledBy<ForecastHandler>()
    .Configure(c =>
    {
        c.MaxRetryAttempts = 2;
        c.RetryDelayBase = TimeSpan.FromSeconds(1);
        // SyncInterval is unused here — the [TimerTrigger] CRON drives cadence.
    })
    .Build();

builder.Services.AddSyncStream<IWeatherApi, AlertDto>("weather", "alerts")
    .WithFetch((api, since, ct) => api.GetAlertsAsync(since, ct))
    .HandledBy<AlertHandler>()
    .Build();

builder.Build().Run();
