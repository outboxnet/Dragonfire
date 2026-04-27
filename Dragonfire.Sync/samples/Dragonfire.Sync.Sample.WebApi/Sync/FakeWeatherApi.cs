namespace Dragonfire.Sync.Sample.WebApi.Sync;

/// <summary>
/// In-memory implementation of <see cref="IWeatherApi"/>. Generates deterministic
/// data so the sample runs without external dependencies.
/// </summary>
public sealed class FakeWeatherApi : IWeatherApi
{
    private static readonly string[] Cities = ["Stockholm", "Berlin", "Tokyo", "Toronto", "Cape Town"];

    public Task<IReadOnlyCollection<ForecastDto>> GetForecastsAsync(DateTime? since, CancellationToken cancellationToken)
    {
        var rng = Random.Shared;
        var observedAt = DateTime.UtcNow;
        var data = Cities.Select(c => new ForecastDto(
            City: c,
            TemperatureC: Math.Round(-5 + rng.NextDouble() * 35, 1),
            ObservedAt: observedAt)).ToArray();
        return Task.FromResult<IReadOnlyCollection<ForecastDto>>(data);
    }

    public Task<IReadOnlyCollection<AlertDto>> GetAlertsAsync(DateTime? since, CancellationToken cancellationToken)
    {
        var rng = Random.Shared;
        // 50% chance of an alert per call, in one or two cities.
        var count = rng.Next(0, 3);
        var alerts = Enumerable.Range(0, count).Select(_ => new AlertDto(
            City: Cities[rng.Next(Cities.Length)],
            Severity: rng.NextDouble() < 0.5 ? "Warning" : "Watch",
            Message: "Heavy precipitation expected.",
            IssuedAt: DateTime.UtcNow)).ToArray();
        return Task.FromResult<IReadOnlyCollection<AlertDto>>(alerts);
    }
}
