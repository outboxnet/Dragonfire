using System.Globalization;
using System.Text;

namespace InboxNet.LoadTests;

/// <summary>
/// Outcome of a load test run. Latencies measure receive-to-handler-completion (end-to-end
/// from the moment the publisher started its POST to the moment the handler returned).
/// </summary>
public sealed class LoadTestResult
{
    public int TotalPublished { get; set; }
    public int PublishFailures { get; set; }
    public int TotalHandled { get; set; }
    public int HandlerFailuresInjected { get; set; }
    public int DuplicatesDetected { get; set; }
    public int UnexpectedHandled { get; set; }
    public int Lost { get; set; }

    public TimeSpan PublishDuration { get; set; }
    public TimeSpan TotalDuration { get; set; }

    public double PublishThroughput =>
        PublishDuration.TotalSeconds > 0 ? TotalPublished / PublishDuration.TotalSeconds : 0;

    public double HandlerThroughput =>
        TotalDuration.TotalSeconds > 0 ? TotalHandled / TotalDuration.TotalSeconds : 0;

    public double[] LatenciesSortedMs { get; set; } = Array.Empty<double>();

    public double LatencyMinMs => LatenciesSortedMs.Length > 0 ? LatenciesSortedMs[0] : 0;
    public double LatencyMaxMs => LatenciesSortedMs.Length > 0 ? LatenciesSortedMs[^1] : 0;
    public double LatencyAvgMs =>
        LatenciesSortedMs.Length > 0 ? LatenciesSortedMs.Average() : 0;

    public double LatencyP50Ms => Percentile(0.50);
    public double LatencyP95Ms => Percentile(0.95);
    public double LatencyP99Ms => Percentile(0.99);

    private double Percentile(double p)
    {
        if (LatenciesSortedMs.Length == 0) return 0;
        var idx = (int)Math.Ceiling(p * LatenciesSortedMs.Length) - 1;
        if (idx < 0) idx = 0;
        if (idx >= LatenciesSortedMs.Length) idx = LatenciesSortedMs.Length - 1;
        return LatenciesSortedMs[idx];
    }

    /// <summary>True when every published message was handled exactly once with no losses.</summary>
    public bool IsCorrect =>
        TotalHandled == TotalPublished &&
        Lost == 0 &&
        UnexpectedHandled == 0;

    public string Format()
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("=== InboxNet Load Test Result ===");
        sb.AppendLine($"  Published:           {TotalPublished} (failures: {PublishFailures})");
        sb.AppendLine($"  Handled:             {TotalHandled}");
        sb.AppendLine($"  Lost:                {Lost}");
        sb.AppendLine($"  Duplicates:          {DuplicatesDetected}");
        sb.AppendLine($"  Unexpected handled:  {UnexpectedHandled}");
        sb.AppendLine($"  Failures injected:   {HandlerFailuresInjected}");
        sb.AppendLine();
        sb.AppendLine($"  Publish duration:    {PublishDuration.TotalSeconds.ToString("F2", ci)} s");
        sb.AppendLine($"  Total duration:      {TotalDuration.TotalSeconds.ToString("F2", ci)} s");
        sb.AppendLine($"  Publish throughput:  {PublishThroughput.ToString("F1", ci)} msg/s");
        sb.AppendLine($"  Handler throughput:  {HandlerThroughput.ToString("F1", ci)} msg/s");
        sb.AppendLine();
        sb.AppendLine("  Latency (receive -> handler complete):");
        sb.AppendLine($"    min: {LatencyMinMs.ToString("F1", ci)} ms");
        sb.AppendLine($"    avg: {LatencyAvgMs.ToString("F1", ci)} ms");
        sb.AppendLine($"    p50: {LatencyP50Ms.ToString("F1", ci)} ms");
        sb.AppendLine($"    p95: {LatencyP95Ms.ToString("F1", ci)} ms");
        sb.AppendLine($"    p99: {LatencyP99Ms.ToString("F1", ci)} ms");
        sb.AppendLine($"    max: {LatencyMaxMs.ToString("F1", ci)} ms");
        sb.AppendLine();
        sb.AppendLine($"  Verdict: {(IsCorrect ? "OK" : "INCORRECT")}");
        return sb.ToString();
    }
}
