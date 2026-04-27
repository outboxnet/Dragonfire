namespace SyncLib.Abstractions;

/// <summary>
/// Consumer-supplied callback invoked by the sync runner after a successful
/// fetch. The handler is responsible for whatever the data should become —
/// upserting into a database, publishing to a queue, writing files, etc.
/// SyncLib never persists data on your behalf.
/// </summary>
/// <typeparam name="TDto">The DTO type returned by the upstream API.</typeparam>
public interface ISyncDataHandler<TDto>
{
    /// <summary>
    /// Process one batch of fetched DTOs. Throw to mark the run as failed and
    /// trigger retry/breaker behaviour.
    /// </summary>
    Task HandleAsync(SyncContext context, IReadOnlyCollection<TDto> data, CancellationToken cancellationToken = default);
}
