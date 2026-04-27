namespace Dragonfire.Poller.Sample.WeatherApi.Weather
{
    /// <summary>
    /// Describes a weather-check polling job: poll Open-Meteo until
    /// current conditions are retrieved for the given coordinates.
    /// </summary>
    public class WeatherPollingRequest
    {
        /// <summary>Latitude in decimal degrees (e.g. 52.52 for Berlin).</summary>
        public double Latitude { get; set; }

        /// <summary>Longitude in decimal degrees (e.g. 13.41 for Berlin).</summary>
        public double Longitude { get; set; }

        /// <summary>Optional human-readable label for the location.</summary>
        public string LocationName { get; set; } = "";
    }
}
