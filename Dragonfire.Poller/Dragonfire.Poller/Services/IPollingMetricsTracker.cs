using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Dragonfire.Poller.Services
{
    // Abstractions/IPollingMetricsTracker.cs (Enhanced)
    public interface IPollingMetricsTracker
    {
        // Core metrics
        void RecordPollingStarted(string pollingType);
        void RecordPollingCompleted(string pollingType, TimeSpan duration);
        void RecordPollingFailed(string pollingType, string failureReason);
        void RecordPollingAttempt(string pollingType, int attemptNumber, TimeSpan duration, bool success);
        void RecordQueueLength(int length);
        void RecordProcessingTime(TimeSpan processingTime);

        // Domain-specific metrics
        void RecordDomainMetric(string pollingType, string metricName, IDictionary<string, double> measurements);
        void RecordDomainMetricWithDimensions(string pollingType, string metricName, double value, IDictionary<string, string> dimensions);
        void TrackDomainEvent(string pollingType, string eventName, IDictionary<string, string> properties);
    }
}
