using Poller.Services;

namespace Poller.Sample.WeatherApi.Weather
{
    /// <summary>
    /// Completes polling as soon as valid weather data is returned.
    /// Never permanently fails — transient errors are handled in the strategy.
    /// </summary>
    public class WeatherPollingCondition : IPollingCondition<WeatherPollingResponse>
    {
        public bool IsComplete(WeatherPollingResponse response)
            => response.FetchedAt > DateTime.MinValue;

        public bool IsFailed(WeatherPollingResponse response)
            => false; // Let the strategy control failure via ShouldContinue
    }
}
