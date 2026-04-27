namespace Dragonfire.Caching.Models;

/// <summary>
/// A request to invalidate cache entries associated with a named operation and its parameters.
/// Enqueued by <see cref="Core.CacheExecutor"/> and processed by <see cref="Core.InvalidationWorker"/>.
/// </summary>
public sealed record InvalidationRequest(string OperationName, object? Parameters);
