using Dragonfire.TraceKit.Abstractions;
using Dragonfire.TraceKit.Models;
using Microsoft.Extensions.Logging;

namespace Dragonfire.TraceKit.Storage;

/// <summary>
/// Default <see cref="ITraceRepository"/>: logs at <c>Debug</c> level and discards.
/// Lets the library start cleanly when the host has not yet wired up a real repository
/// — production hosts SHOULD register their own implementation.
/// </summary>
public sealed class NullTraceRepository : ITraceRepository
{
    private readonly ILogger<NullTraceRepository> _logger;

    public NullTraceRepository(ILogger<NullTraceRepository> logger) => _logger = logger;

    /// <inheritdoc />
    public Task SaveAsync(ApiTrace trace, CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "TraceKit (no repository registered) {Kind} {Method} {Url} -> {Status} in {Duration}ms (CorrelationId={CorrelationId} Seq={Sequence})",
                trace.Kind, trace.Method, trace.Url, trace.StatusCode, (int)trace.Duration.TotalMilliseconds,
                trace.CorrelationId, trace.Sequence);
        }
        return Task.CompletedTask;
    }
}
