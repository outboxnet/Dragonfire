namespace Dragonfire.Poller.Services
{
    /// <summary>
    /// A no-operation metrics tracker that discards all telemetry.
    /// This is the default implementation used when no metrics backend is configured.
    /// Replace it by registering <c>AppInsightsMetricsTracker</c> or your own
    /// <see cref="IPollingMetricsTracker"/> implementation.
    /// </summary>
    public sealed class NoOpMetricsTracker : IPollingMetricsTracker
    {
        public void RecordPollingStarted(string pollingType) { }
        public void RecordPollingCompleted(string pollingType, TimeSpan duration) { }
        public void RecordPollingFailed(string pollingType, string failureReason) { }
        public void RecordPollingAttempt(string pollingType, int attemptNumber, TimeSpan duration, bool success) { }
        public void RecordQueueLength(int length) { }
        public void RecordProcessingTime(TimeSpan processingTime) { }
        public void RecordDomainMetric(string pollingType, string metricName, IDictionary<string, double> measurements) { }
        public void RecordDomainMetricWithDimensions(string pollingType, string metricName, double value, IDictionary<string, string> dimensions) { }
        public void TrackDomainEvent(string pollingType, string eventName, IDictionary<string, string> properties) { }
    }
}
