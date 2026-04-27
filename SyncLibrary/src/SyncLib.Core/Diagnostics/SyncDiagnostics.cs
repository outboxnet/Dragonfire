using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace SyncLib.Core.Diagnostics;

/// <summary>
/// Static <see cref="ActivitySource"/> and <see cref="Meter"/> used by SyncLib.
/// Subscribe in your hosting app to forward to OpenTelemetry, App Insights, etc.
/// </summary>
public static class SyncDiagnostics
{
    /// <summary>Logical name of the source/meter — register this with OpenTelemetry.</summary>
    public const string SourceName = "SyncLib";

    private static readonly string Version =
        typeof(SyncDiagnostics).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(SyncDiagnostics).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    /// <summary>ActivitySource for sync runs.</summary>
    public static readonly ActivitySource ActivitySource = new(SourceName, Version);

    /// <summary>Meter that exposes sync metrics.</summary>
    public static readonly Meter Meter = new(SourceName, Version);

    internal static readonly Histogram<double> SyncDuration =
        Meter.CreateHistogram<double>("synclib.sync.duration", unit: "ms", description: "Duration of sync runs in milliseconds.");

    internal static readonly Counter<long> SyncSuccesses =
        Meter.CreateCounter<long>("synclib.sync.successes", description: "Number of successful sync runs.");

    internal static readonly Counter<long> SyncFailures =
        Meter.CreateCounter<long>("synclib.sync.failures", description: "Number of failed sync runs (after retries).");

    internal static readonly Counter<long> SyncRecords =
        Meter.CreateCounter<long>("synclib.sync.records", description: "Number of records persisted across sync runs.");

    internal static readonly Counter<long> SyncSkipped =
        Meter.CreateCounter<long>("synclib.sync.skipped", description: "Number of sync runs skipped (e.g. circuit-breaker open).");
}
