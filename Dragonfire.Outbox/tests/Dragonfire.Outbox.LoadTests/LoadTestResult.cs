namespace Dragonfire.Outbox.LoadTests;

/// <summary>
/// Immutable snapshot of all load test metrics, computed after the drain phase.
/// </summary>
public sealed class LoadTestResult
{
    public required LoadTestConfig Config { get; init; }

    // ── Throughput ────────────────────────────────────────────────────────────
    public int    TotalPublished      { get; init; }
    public int    TotalDelivered      { get; init; }   // unique correlation IDs received
    public int    TotalRetryDeliveries{ get; init; }   // delivery attempts beyond the first

    public double PublishThroughput   { get; init; }   // msg/s during publish phase
    public double DeliveryThroughput  { get; init; }   // msg/s from first publish to last delivery

    public double PublishDurationSec  { get; init; }
    public double TotalDurationSec    { get; init; }

    // ── Correctness ───────────────────────────────────────────────────────────
    public int LostCount        { get; init; }   // published but never received
    public int DuplicateCount   { get; init; }   // correlation IDs received more than once
    public int UnexpectedCount  { get; init; }   // received but never published (shouldn't happen)
    public long HmacRejections  { get; init; }   // HMAC mismatch (signing bug)
    public long FailuresInjected{ get; init; }   // 503 injections by the receiver

    // ── Latency ───────────────────────────────────────────────────────────────
    // Per-message ms = receivedAt − max(commitAt, dispatchStart).
    // In coupled (hot-path) mode dispatchStart ≤ all commits, so this reduces
    // to the true end-to-end commit → webhook-receipt latency.
    public double[] LatenciesSorted { get; init; } = [];

    public double LatencyMin => LatenciesSorted.Length > 0 ? LatenciesSorted[0]    : 0;
    public double LatencyAvg => LatenciesSorted.Length > 0 ? LatenciesSorted.Average() : 0;
    public double LatencyP50 => Percentile(50);
    public double LatencyP95 => Percentile(95);
    public double LatencyP99 => Percentile(99);
    public double LatencyMax => LatenciesSorted.Length > 0 ? LatenciesSorted[^1]   : 0;

    private double Percentile(int pct)
    {
        if (LatenciesSorted.Length == 0) return 0;
        var idx = (int)Math.Ceiling(pct / 100.0 * LatenciesSorted.Length) - 1;
        return LatenciesSorted[Math.Clamp(idx, 0, LatenciesSorted.Length - 1)];
    }

    // ── Verdict ───────────────────────────────────────────────────────────────
    /// <summary>
    /// True when every published message was received exactly once and HMAC matched.
    /// With failure-rate > 0 some messages will have retry deliveries, but the
    /// unique-delivery count must still equal the published count.
    /// </summary>
    public bool IsCorrect =>
        LostCount == 0 && DuplicateCount == 0 && UnexpectedCount == 0 && HmacRejections == 0;

    // ─────────────────────────────────────────────────────────────────────────

    public void Print()
    {
        const string sep = "══════════════════════════════════════════════════════════════";

        Console.WriteLine();
        Console.WriteLine(sep);
        Console.WriteLine("  OUTBOXNET LOAD TEST — RESULTS");
        Console.WriteLine(sep);

        Console.WriteLine();
        Console.WriteLine("  CONFIGURATION");
        Config.Print();

        Console.WriteLine();
        Console.WriteLine("  THROUGHPUT");
        Console.WriteLine($"    Publish phase:      {TotalPublished:N0} messages in {PublishDurationSec:F1} s");
        Console.WriteLine($"    Publish rate:       {PublishThroughput:F1} msg/s");
        Console.WriteLine($"    Delivered (unique): {TotalDelivered:N0} messages");
        Console.WriteLine($"    Delivery rate:      {DeliveryThroughput:F1} msg/s  (wall-clock: publish→last delivery)");
        Console.WriteLine($"    Total wall clock:   {TotalDurationSec:F1} s");
        if (FailuresInjected > 0)
            Console.WriteLine($"    Failures injected:  {FailuresInjected:N0}  (receiver returned 503 to trigger retries)");
        if (TotalRetryDeliveries > 0)
            Console.WriteLine($"    Retry deliveries:   {TotalRetryDeliveries:N0}  (extra HTTP calls due to retries)");

        Console.WriteLine();
        Console.WriteLine("  LATENCY   (publish commit → webhook receipt; hot path active)");
        Console.WriteLine($"    Min   {LatencyMin,8:F0} ms");
        Console.WriteLine($"    Avg   {LatencyAvg,8:F0} ms");
        Console.WriteLine($"    P50   {LatencyP50,8:F0} ms");
        Console.WriteLine($"    P95   {LatencyP95,8:F0} ms");
        Console.WriteLine($"    P99   {LatencyP99,8:F0} ms");
        Console.WriteLine($"    Max   {LatencyMax,8:F0} ms");

        Console.WriteLine();
        Console.WriteLine("  CORRECTNESS");
        PrintCheck("Published",          $"{TotalPublished:N0}",   null);
        PrintCheck("Delivered (unique)", $"{TotalDelivered:N0}",   null);
        PrintCheck("Lost",               $"{LostCount:N0}",        LostCount == 0);
        PrintCheck("Duplicates",         $"{DuplicateCount:N0}",   DuplicateCount == 0);
        PrintCheck("Unexpected",         $"{UnexpectedCount:N0}",  UnexpectedCount == 0);
        PrintCheck("HMAC rejections",    $"{HmacRejections:N0}",   HmacRejections == 0);

        Console.WriteLine();
        var verdict = IsCorrect ? "PASS" : "FAIL";
        var color   = IsCorrect ? ConsoleColor.Green : ConsoleColor.Red;
        var original = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine($"  OVERALL: {verdict}  —  " +
            (IsCorrect
                ? "All messages delivered correctly, no duplicates."
                : "Correctness violations detected — see above."));
        Console.ForegroundColor = original;
        Console.WriteLine(sep);
    }

    private static void PrintCheck(string label, string value, bool? ok)
    {
        var icon = ok switch { true => "✓", false => "✗", null => " " };
        var color = ok switch { true => ConsoleColor.Green, false => ConsoleColor.Red, null => Console.ForegroundColor };
        var original = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine($"    {icon}  {label,-24} {value}");
        Console.ForegroundColor = original;
    }
}
