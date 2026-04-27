using System.Collections.Concurrent;

namespace Dragonfire.Poller.Services
{
    // Thread-safe metrics aggregator
    public class ThreadSafeMetricsAggregator
    {
        private readonly ConcurrentDictionary<string, long> _counters = new();
        private readonly ConcurrentDictionary<string, double> _gauges = new();
        private readonly ConcurrentDictionary<string, ConcurrentBag<double>> _histograms = new();
        private readonly ReaderWriterLockSlim _lock = new();

        public void IncrementCounter(string key, long value = 1)
        {
            _counters.AddOrUpdate(key, value, (_, existing) => existing + value);
        }

        public void SetGauge(string key, double value)
        {
            _gauges.AddOrUpdate(key, value, (_, _) => value);
        }

        public void RecordHistogram(string key, double value)
        {
            var bag = _histograms.GetOrAdd(key, _ => new ConcurrentBag<double>());
            bag.Add(value);

            // Trim old data periodically (keep last 1000)
            if (bag.Count > 1000)
            {
                Task.Run(() => TrimHistogram(key, bag));
            }
        }

        private void TrimHistogram(string key, ConcurrentBag<double> bag)
        {
            _lock.EnterWriteLock();
            try
            {
                while (bag.Count > 900)
                {
                    bag.TryTake(out _);
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public Dictionary<string, object> GetMetrics()
        {
            var result = new Dictionary<string, object>();

            foreach (var counter in _counters)
                result[$"counter_{counter.Key}"] = counter.Value;

            foreach (var gauge in _gauges)
                result[$"gauge_{gauge.Key}"] = gauge.Value;

            foreach (var histogram in _histograms)
            {
                var values = histogram.Value.ToList();
                if (values.Any())
                {
                    result[$"histogram_{histogram.Key}_count"] = values.Count;
                    result[$"histogram_{histogram.Key}_avg"] = values.Average();
                    result[$"histogram_{histogram.Key}_p95"] = CalculatePercentile(values, 95);
                    result[$"histogram_{histogram.Key}_p99"] = CalculatePercentile(values, 99);
                }
            }

            return result;
        }

        private double CalculatePercentile(List<double> values, int percentile)
        {
            var sorted = values.OrderBy(v => v).ToList();
            var index = (int)Math.Ceiling(percentile / 100.0 * sorted.Count) - 1;
            return sorted[Math.Max(0, index)];
        }
    }
}
