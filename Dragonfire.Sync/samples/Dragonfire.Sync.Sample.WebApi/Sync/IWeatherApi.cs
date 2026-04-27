namespace Dragonfire.Sync.Sample.WebApi.Sync;

/// <summary>
/// Stand-in for a real HTTP weather API client. Returns multiple kinds of data
/// (forecasts and alerts) so the sample demonstrates a single client backing
/// multiple sync streams.
/// </summary>
public interface IWeatherApi
{
    Task<IReadOnlyCollection<ForecastDto>> GetForecastsAsync(DateTime? since, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<AlertDto>> GetAlertsAsync(DateTime? since, CancellationToken cancellationToken);
}

/// <summary>Single forecast observation. The shape we care about for the sync — not a database row.</summary>
public sealed record ForecastDto(string City, double TemperatureC, DateTime ObservedAt);

/// <summary>Severe-weather alert. Independent of forecasts and synced on its own schedule.</summary>
public sealed record AlertDto(string City, string Severity, string Message, DateTime IssuedAt);
