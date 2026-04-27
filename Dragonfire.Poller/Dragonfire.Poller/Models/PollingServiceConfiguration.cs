using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Dragonfire.Poller.Models
{
    public class PollingServiceConfiguration
    {
        public int MaxConcurrentPollings { get; set; } = 100;
        public int QueueCapacity { get; set; } = 10000;
        public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromMinutes(5);
        public int DefaultMaxAttempts { get; set; } = 30;
        public bool EnableDetailedMetrics { get; set; } = true;
        public TimeSpan? DataRetentionPeriod { get; set; } = TimeSpan.FromHours(24);
        public int MetricsAggregationIntervalSeconds { get; set; } = 60;
        public bool EnableMetricsAggregation { get; set; } = true;
    }
}
