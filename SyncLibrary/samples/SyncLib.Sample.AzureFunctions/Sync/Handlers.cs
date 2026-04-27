using Microsoft.Extensions.Logging;
using SyncLib.Abstractions;

namespace SyncLib.Sample.AzureFunctions.Sync;

/// <summary>
/// Consumer-owned handler for forecasts. Replace the in-memory log with real
/// persistence (CosmosDB, Table Storage, SQL via EF Core, ...). The handler
/// gets full <see cref="SyncContext"/> so it can fan out per-source if needed.
/// </summary>
public sealed class ForecastHandler : ISyncDataHandler<ForecastDto>
{
    private readonly ILogger<ForecastHandler> _logger;

    public ForecastHandler(ILogger<ForecastHandler> logger) => _logger = logger;

    public Task HandleAsync(SyncContext context, IReadOnlyCollection<ForecastDto> data, CancellationToken cancellationToken = default)
    {
        foreach (var dto in data)
        {
            _logger.LogInformation("[{Stream}] forecast {City} = {Temp}°C @ {At:o}",
                context.Key, dto.City, dto.TemperatureC, dto.ObservedAt);
        }
        return Task.CompletedTask;
    }
}

public sealed class AlertHandler : ISyncDataHandler<AlertDto>
{
    private readonly ILogger<AlertHandler> _logger;

    public AlertHandler(ILogger<AlertHandler> logger) => _logger = logger;

    public Task HandleAsync(SyncContext context, IReadOnlyCollection<AlertDto> data, CancellationToken cancellationToken = default)
    {
        foreach (var dto in data)
        {
            _logger.LogWarning("[{Stream}] alert {Severity} for {City}: {Message}",
                context.Key, dto.Severity, dto.City, dto.Message);
        }
        return Task.CompletedTask;
    }
}
