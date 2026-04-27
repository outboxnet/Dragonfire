using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Dragonfire.Poller.Models;
using System.Collections.Concurrent;

namespace Dragonfire.Poller.Services
{
    // Enhanced App Insights Implementation
    public class AppInsightsMetricsTracker : IPollingMetricsTracker
    {
        private readonly TelemetryClient _telemetryClient;
        private readonly ILogger<AppInsightsMetricsTracker> _logger;
        private readonly ThreadSafeMetricsAggregator _aggregator;
        private readonly ConcurrentDictionary<string, long> _activePollings = new();
        private readonly Timer? _aggregationTimer;

        public AppInsightsMetricsTracker(
            TelemetryClient telemetryClient,
            ILogger<AppInsightsMetricsTracker> logger,
            IOptions<PollingServiceConfiguration> configuration)
        {
            _telemetryClient = telemetryClient;
            _logger = logger;
            _aggregator = new ThreadSafeMetricsAggregator();

            // Send aggregated metrics every minute
            if (configuration.Value.EnableDetailedMetrics)
            {
                _aggregationTimer = new Timer(SendAggregatedMetrics, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
            }
        }

        public void RecordPollingStarted(string pollingType)
        {
            var count = _activePollings.AddOrUpdate(pollingType, 1, (_, val) => val + 1);

            _telemetryClient.TrackEvent("PollingStarted", new Dictionary<string, string>
            {
                ["PollingType"] = pollingType
            });

            _telemetryClient.GetMetric("PollingActiveCount", "PollingType").TrackValue(count, pollingType);
            _aggregator.IncrementCounter($"polling_started_{pollingType}");
        }

        public void RecordPollingCompleted(string pollingType, TimeSpan duration)
        {
            var count = _activePollings.AddOrUpdate(pollingType, 0, (_, val) => Math.Max(0, val - 1));

            _telemetryClient.TrackEvent("PollingCompleted", new Dictionary<string, string>
            {
                ["PollingType"] = pollingType,
                ["Duration"] = duration.ToString()
            });

            _telemetryClient.GetMetric("PollingDuration", "PollingType").TrackValue(duration.TotalSeconds, pollingType);
            _aggregator.IncrementCounter($"polling_completed_{pollingType}");
            _aggregator.RecordHistogram($"polling_duration_{pollingType}", duration.TotalSeconds);
        }

        public void RecordPollingFailed(string pollingType, string failureReason)
        {
            _telemetryClient.TrackEvent("PollingFailed", new Dictionary<string, string>
            {
                ["PollingType"] = pollingType,
                ["FailureReason"] = failureReason
            });

            _telemetryClient.GetMetric("PollingFailureRate", "PollingType", "FailureReason")
                .TrackValue(1, pollingType, failureReason);

            _aggregator.IncrementCounter($"polling_failed_{pollingType}_{failureReason}");
        }

        public void RecordPollingAttempt(string pollingType, int attemptNumber, TimeSpan duration, bool success)
        {
            var properties = new Dictionary<string, string>
            {
                ["PollingType"] = pollingType,
                ["AttemptNumber"] = attemptNumber.ToString(),
                ["Success"] = success.ToString()
            };

            _telemetryClient.TrackEvent("PollingAttempt", properties);
            _telemetryClient.GetMetric("PollingAttemptDuration", "PollingType").TrackValue(duration.TotalSeconds, pollingType);

            _aggregator.RecordHistogram($"attempt_duration_{pollingType}", duration.TotalSeconds);
            if (success)
                _aggregator.IncrementCounter($"attempt_success_{pollingType}");
            else
                _aggregator.IncrementCounter($"attempt_failure_{pollingType}");
        }

        public void RecordQueueLength(int length)
        {
            _telemetryClient.GetMetric("PollingQueueLength").TrackValue(length);
            _aggregator.SetGauge("queue_length", length);
        }

        public void RecordProcessingTime(TimeSpan processingTime)
        {
            _telemetryClient.GetMetric("PollingProcessingTime").TrackValue(processingTime.TotalSeconds);
            _aggregator.RecordHistogram("processing_time", processingTime.TotalSeconds);
        }

        // Domain-specific metrics implementation
        public void RecordDomainMetric(string pollingType, string metricName, IDictionary<string, double> measurements)
        {
            var metric = _telemetryClient.GetMetric(metricName, "PollingType");

            foreach (var measurement in measurements)
            {
                metric.TrackValue(measurement.Value, pollingType);
                _aggregator.RecordHistogram($"{metricName}_{measurement.Key}_{pollingType}", measurement.Value);
            }
        }

        public void RecordDomainMetricWithDimensions(string pollingType, string metricName, double value, IDictionary<string, string> dimensions)
        {
            var allDimensions = new Dictionary<string, string>(dimensions)
            {
                ["PollingType"] = pollingType
            };

            foreach (var item in allDimensions)
            {
                var metric = _telemetryClient.GetMetric(metricName, item.Key);
                metric.TrackValue(value, item.Value);
            }

            var dimensionKey = string.Join("_", allDimensions.Values);
            _aggregator.RecordHistogram($"{metricName}_{dimensionKey}", value);
        }

        public void TrackDomainEvent(string pollingType, string eventName, IDictionary<string, string> properties)
        {
            var allProperties = new Dictionary<string, string>(properties)
            {
                ["PollingType"] = pollingType,
                ["EventName"] = eventName
            };

            _telemetryClient.TrackEvent($"Domain_{eventName}", allProperties);
            _aggregator.IncrementCounter($"domain_event_{pollingType}_{eventName}");
        }

        private void SendAggregatedMetrics(object? state)
        {
            try
            {
                var metrics = _aggregator.GetMetrics();
                foreach (var metric in metrics)
                {
                    _telemetryClient.TrackTrace($"Aggregated Metric: {metric.Key} = {metric.Value}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send aggregated metrics");
            }
        }
    }
}
