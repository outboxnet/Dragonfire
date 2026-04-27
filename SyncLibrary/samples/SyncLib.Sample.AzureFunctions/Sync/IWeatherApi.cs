namespace SyncLib.Sample.AzureFunctions.Sync;

/// <summary>Stand-in for a real HTTP weather API client.</summary>
public interface IWeatherApi
{
    Task<IReadOnlyCollection<ForecastDto>> GetForecastsAsync(DateTime? since, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<AlertDto>> GetAlertsAsync(DateTime? since, CancellationToken cancellationToken);
}

public sealed record ForecastDto(string City, double TemperatureC, DateTime ObservedAt);
public sealed record AlertDto(string City, string Severity, string Message, DateTime IssuedAt);

public sealed class FakeWeatherApi : IWeatherApi
{
    private static readonly string[] Cities = ["Stockholm", "Berlin", "Tokyo"];

    public Task<IReadOnlyCollection<ForecastDto>> GetForecastsAsync(DateTime? since, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var data = Cities.Select(c => new ForecastDto(c, Random.Shared.NextDouble() * 30, now)).ToArray();
        return Task.FromResult<IReadOnlyCollection<ForecastDto>>(data);
    }

    public Task<IReadOnlyCollection<AlertDto>> GetAlertsAsync(DateTime? since, CancellationToken cancellationToken)
    {
        var alerts = Random.Shared.Next(0, 2) == 1
            ? new[] { new AlertDto("Stockholm", "Watch", "Storm developing.", DateTime.UtcNow) }
            : Array.Empty<AlertDto>();
        return Task.FromResult<IReadOnlyCollection<AlertDto>>(alerts);
    }
}
