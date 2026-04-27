using System.Collections.Concurrent;
using SagaNet.Core.Abstractions;

namespace SagaNet.Core.Events;

/// <summary>
/// In-process event broker backed by <see cref="TaskCompletionSource{T}"/>.
/// Suitable for single-host deployments.
/// For multi-host deployments, events are persisted via <see cref="IWorkflowRepository"/>
/// and polled by the host — no additional broker is required.
/// </summary>
public sealed class InMemoryEventBroker : IEventBroker
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<object?>> _waiters = new();

    public Task PublishAsync(
        string eventName,
        string eventKey,
        object? data,
        CancellationToken ct = default)
    {
        var key = BuildKey(eventName, eventKey);

        if (_waiters.TryRemove(key, out var tcs))
            tcs.TrySetResult(data);

        return Task.CompletedTask;
    }

    public async Task<object?> SubscribeAsync(
        string eventName,
        string eventKey,
        CancellationToken ct = default)
    {
        var key = BuildKey(eventName, eventKey);
        var tcs = _waiters.GetOrAdd(key, _ => new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously));

        await using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
        return await tcs.Task;
    }

    private static string BuildKey(string eventName, string eventKey)
        => $"{eventName}:{eventKey}";
}
