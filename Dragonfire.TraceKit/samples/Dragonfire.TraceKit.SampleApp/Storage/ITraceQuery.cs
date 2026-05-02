using Dragonfire.TraceKit.Models;

namespace Dragonfire.TraceKit.SampleApp.Storage;

/// <summary>
/// Read-side projection over captured traces, consumed by the MVC viewer. Splitting it
/// from <see cref="Dragonfire.TraceKit.Abstractions.ITraceRepository"/> keeps the write
/// contract minimal — repositories that persist to SQL, blob, or queue do not have to
/// implement query support to be usable.
/// </summary>
public interface ITraceQuery
{
    /// <summary>One row per inbound request (correlation id), newest first.</summary>
    IReadOnlyList<TraceSession> ListSessions(int take = 100);

    /// <summary>All traces for a single correlation id, ordered by sequence.</summary>
    IReadOnlyList<ApiTrace> GetSession(string correlationId);
}

/// <summary>Lightweight summary used by the sessions index view.</summary>
public sealed record TraceSession(
    string CorrelationId,
    DateTimeOffset StartedAtUtc,
    string Method,
    string Url,
    int? StatusCode,
    int OutboundCallCount,
    TimeSpan Duration,
    string? TenantId);
