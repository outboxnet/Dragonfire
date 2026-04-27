using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core.Serialization;
using Microsoft.Extensions.Logging;
using Dragonfire.Poller.Models;
using Dragonfire.Poller.Services;

namespace Dragonfire.Poller.Sample.WeatherApi.Weather
{
    /// <summary>
    /// Fetches current weather from the free Open-Meteo API (no API key required).
    /// Returns <see cref="PollingResult{T}.Failure"/> with <c>shouldContinue: true</c>
    /// on transient errors so the framework retries automatically with exponential backoff.
    /// </summary>
    public class WeatherPollingStrategy : IPollingStrategy<WeatherPollingRequest, WeatherPollingResponse>
    {
        private const string BaseUrl = "https://api.open-meteo.com/v1/forecast";

        private readonly HttpClient _http;
        private readonly ILogger<WeatherPollingStrategy> _logger;

        public WeatherPollingStrategy(HttpClient http, ILogger<WeatherPollingStrategy> logger)
        {
            _http = http;
            _logger = logger;
        }

        public async Task<PollingResult<WeatherPollingResponse>> PollAsync(
            WeatherPollingRequest request,
            CancellationToken cancellationToken)
        {
            var url = $"{BaseUrl}" +
                      $"?latitude={request.Latitude}" +
                      $"&longitude={request.Longitude}" +
                      $"&current=temperature_2m,precipitation,weathercode" +
                      $"&hourly=precipitation_probability" +
                      $"&forecast_days=1" +
                      $"&timezone=UTC";

            try
            {
                var raw = await _http.GetStringAsync(url, cancellationToken);

                var content = JsonSerializer.Deserialize<List<OpenMeteoResponse>>(raw, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }).First();

                var data = content;

                if (data.current is null)
                    return PollingResult<WeatherPollingResponse>.Failure(
                        "Open-Meteo returned an empty response", shouldContinue: true);

                // Take max rain probability over the next 12 hours
                var now = DateTime.UtcNow;
                var maxRain = data?.hourly?.precipitation_probability
                    ?.Zip(data.hourly.time, (prob, timeStr) => (prob, timeStr))
                    .Where(x => DateTime.TryParse(x.timeStr, out var t) && t >= now && t <= now.AddHours(12))
                    .Select(x => x.prob ?? 0)
                    .DefaultIfEmpty(0)
                    .Max() ?? 0;

                var response = new WeatherPollingResponse
                {
                    Latitude                  = data.latitude,
                    Longitude                 = data.longitude,
                    LocationName              = request.LocationName,
                    TemperatureCelsius        = data.current.temperature_2m,
                    PrecipitationMm           = data.current.precipitation,
                    WeatherCode               = data.current.weathercode,
                    MaxRainProbabilityPercent = maxRain,
                    Condition                 = InterpretWeatherCode(data.current.weathercode),
                    FetchedAt                 = DateTime.UtcNow
                };

                return PollingResult<WeatherPollingResponse>.Complete(response);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Transient HTTP error fetching weather for {Location}", request.LocationName);
                return PollingResult<WeatherPollingResponse>.Failure(
                    $"HTTP error: {ex.Message}", shouldContinue: true);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Timeout fetching weather for {Location}", request.LocationName);
                return PollingResult<WeatherPollingResponse>.Failure(
                    "Request timed out", shouldContinue: true);
            }
        }

        private static string InterpretWeatherCode(int code) => code switch
        {
            0           => "Clear sky",
            1           => "Mainly clear",
            2           => "Partly cloudy",
            3           => "Overcast",
            45 or 48    => "Fog",
            51 or 53 or 55 => "Drizzle",
            61 or 63 or 65 => "Rain",
            71 or 73 or 75 => "Snow",
            80 or 81 or 82 => "Rain showers",
            95          => "Thunderstorm",
            _           => "Unknown"
        };
    }
}
