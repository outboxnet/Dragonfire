using Poller.Services;

namespace Poller.Extensions
{
    /// <summary>
    /// Convenience helpers for recording domain-specific metrics through <see cref="IPollingMetricsTracker"/>.
    /// </summary>
    public static class DomainMetricsExtensions
    {
        /// <summary>Records a single numeric measurement for a domain event.</summary>
        public static void RecordMetric(
            this IPollingMetricsTracker tracker,
            string pollingType,
            string metricName,
            double value)
        {
            tracker.RecordDomainMetric(pollingType, metricName,
                new Dictionary<string, double> { [metricName] = value });
        }

        /// <summary>Records a timing metric (in seconds) for a domain operation.</summary>
        public static void RecordTiming(
            this IPollingMetricsTracker tracker,
            string pollingType,
            string operationName,
            TimeSpan duration)
        {
            tracker.RecordDomainMetric(pollingType, $"{operationName}Duration",
                new Dictionary<string, double> { ["Seconds"] = duration.TotalSeconds });
        }

        /// <summary>Emits a domain event with a single string property.</summary>
        public static void TrackEvent(
            this IPollingMetricsTracker tracker,
            string pollingType,
            string eventName,
            string key,
            string value)
        {
            tracker.TrackDomainEvent(pollingType, eventName,
                new Dictionary<string, string> { [key] = value });
        }
    }
}
