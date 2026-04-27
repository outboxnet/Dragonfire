using System.Collections.Concurrent;
using System.Diagnostics;

namespace InboxNet.LoadTests;

/// <summary>
/// Shared state between the publisher (records send time per event id) and the handler
/// (records completion time and increments counters). Resolved as a singleton from DI so
/// the test runner and the inbox handler observe the same instance.
/// </summary>
public sealed class LoadTestTracker
{
    private readonly ConcurrentDictionary<string, long> _sentTicks = new();
    private readonly ConcurrentBag<double> _latenciesMs = new();
    private readonly HashSet<string> _expected = new();
    private readonly object _expectedLock = new();
    private readonly ConcurrentDictionary<string, byte> _handled = new();

    private long _handledCount;
    private long _failuresInjected;
    private long _unexpectedHandled;
    private long _duplicatesDetected;

    public double FailureRate { get; set; }

    public int Handled => (int)Interlocked.Read(ref _handledCount);
    public int FailuresInjected => (int)Interlocked.Read(ref _failuresInjected);
    public int UnexpectedHandled => (int)Interlocked.Read(ref _unexpectedHandled);
    public int DuplicatesDetected => (int)Interlocked.Read(ref _duplicatesDetected);

    public void RegisterExpected(string eventId)
    {
        lock (_expectedLock)
        {
            _expected.Add(eventId);
        }
    }

    public void MarkSent(string eventId)
    {
        _sentTicks[eventId] = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Returns true if the handler should throw on this attempt (deterministic per-eventId
    /// failure injection that only fires on attempt 0 so the message eventually succeeds).
    /// </summary>
    public bool ShouldFail(string eventId, int attempt)
    {
        if (FailureRate <= 0 || attempt > 0) return false;

        // Stable per-eventId hash so each load test run injects failures at the same rate
        // but on a deterministic subset (avoids flaky variance between runs).
        var hash = (uint)eventId.GetHashCode() & 0xFFFFFF;
        var threshold = (uint)(FailureRate * 0xFFFFFF);
        var fail = hash < threshold;
        if (fail) Interlocked.Increment(ref _failuresInjected);
        return fail;
    }

    public void MarkHandled(string eventId)
    {
        if (!_handled.TryAdd(eventId, 0))
        {
            Interlocked.Increment(ref _duplicatesDetected);
            return;
        }

        Interlocked.Increment(ref _handledCount);

        bool wasExpected;
        lock (_expectedLock)
        {
            wasExpected = _expected.Contains(eventId);
        }
        if (!wasExpected)
        {
            Interlocked.Increment(ref _unexpectedHandled);
        }

        if (_sentTicks.TryGetValue(eventId, out var startTicks))
        {
            var elapsedMs = (Stopwatch.GetTimestamp() - startTicks)
                * 1000.0 / Stopwatch.Frequency;
            _latenciesMs.Add(elapsedMs);
        }
    }

    public double[] DrainLatenciesSorted()
    {
        var arr = _latenciesMs.ToArray();
        Array.Sort(arr);
        return arr;
    }

    public int LostCount()
    {
        lock (_expectedLock)
        {
            var lost = 0;
            foreach (var id in _expected)
            {
                if (!_handled.ContainsKey(id)) lost++;
            }
            return lost;
        }
    }
}
