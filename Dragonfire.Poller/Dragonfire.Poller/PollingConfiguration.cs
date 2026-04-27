namespace Dragonfire.Poller
{
    // Domain/Models/PollingConfiguration.cs
    public class PollingConfiguration
    {
        public int MaxAttempts { get; set; } = 30;
        public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);
        public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);
        public double BackoffMultiplier { get; set; } = 2.0;
        public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);
    }
}
