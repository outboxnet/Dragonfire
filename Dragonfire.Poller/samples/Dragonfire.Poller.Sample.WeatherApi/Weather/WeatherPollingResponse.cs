namespace Dragonfire.Poller.Sample.WeatherApi.Weather
{
    /// <summary>Current weather conditions returned by Open-Meteo.</summary>
    public class WeatherPollingResponse
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string LocationName { get; set; } = "";

        /// <summary>Current air temperature in °C.</summary>
        public decimal TemperatureCelsius { get; set; }

        /// <summary>Precipitation in mm for the current hour.</summary>
        public decimal PrecipitationMm { get; set; }

        /// <summary>Max precipitation probability (%) across the next 12 hours.</summary>
        public int MaxRainProbabilityPercent { get; set; }

        /// <summary>WMO weather interpretation code.</summary>
        public int WeatherCode { get; set; }

        /// <summary>Human-readable summary derived from the weather code.</summary>
        public string Condition { get; set; } = "";

        public DateTime FetchedAt { get; set; }
    }

    // ── Open-Meteo JSON deserialization models ────────────────────────────────

    internal class OpenMeteoResponse
    {
        public double latitude { get; set; }
        public double longitude { get; set; }
        public OpenMeteoCurrent? current { get; set; }
        public OpenMeteoHourly? hourly { get; set; }
    }

    internal class OpenMeteoCurrent
    {
        public decimal temperature_2m { get; set; }
        public decimal precipitation { get; set; }
        public int weathercode { get; set; }
    }

    internal class OpenMeteoHourly
    {
        public List<string> time { get; set; } = new();
        public List<int?> precipitation_probability { get; set; } = new();
    }
}
