using Dragonfire.TraceKit.Models;

namespace Dragonfire.TraceKit.Abstractions;

/// <summary>
/// Persists captured <see cref="ApiTrace"/> instances. Implementations decide the storage
/// engine (SQL Server, Postgres, Cosmos, blob, queue, …) and the entity mapping; the
/// library does not reference EF Core or any storage provider.
/// </summary>
/// <remarks>
/// Implementations are invoked by a single background consumer (one item at a time) and
/// MUST NOT throw — exceptions are caught and logged but otherwise swallowed so that
/// telemetry failures never affect production traffic. Be fast: long-running writes will
/// build channel pressure and eventually drop traces.
/// </remarks>
public interface ITraceRepository
{
    /// <summary>Persists a single trace.</summary>
    Task SaveAsync(ApiTrace trace, CancellationToken cancellationToken);
}
