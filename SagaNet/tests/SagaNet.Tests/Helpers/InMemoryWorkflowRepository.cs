using System.Collections.Concurrent;
using SagaNet.Core.Abstractions;
using SagaNet.Core.Models;

namespace SagaNet.Tests.Helpers;

/// <summary>Thread-safe in-memory repository for unit tests.</summary>
public sealed class InMemoryWorkflowRepository : IWorkflowRepository
{
    private readonly ConcurrentDictionary<Guid, WorkflowInstance> _instances = new();
    private readonly ConcurrentDictionary<Guid, WorkflowEvent> _events = new();
    private readonly object _acquireLock = new();

    public Task<WorkflowInstance> CreateInstanceAsync(WorkflowInstance instance, CancellationToken ct = default)
    {
        _instances[instance.Id] = instance;
        return Task.FromResult(instance);
    }

    public Task<WorkflowInstance?> GetInstanceAsync(Guid instanceId, CancellationToken ct = default)
        => Task.FromResult(_instances.TryGetValue(instanceId, out var i) ? i : null);

    public Task<WorkflowInstance?> TryAcquireInstanceAsync(Guid instanceId, CancellationToken ct = default)
    {
        lock (_acquireLock)
        {
            if (!_instances.TryGetValue(instanceId, out var instance)) return Task.FromResult<WorkflowInstance?>(null);
            if (instance.Status != WorkflowStatus.Runnable) return Task.FromResult<WorkflowInstance?>(null);

            instance.Status = WorkflowStatus.Running;
            return Task.FromResult<WorkflowInstance?>(instance);
        }
    }

    public Task UpdateInstanceAsync(WorkflowInstance instance, CancellationToken ct = default)
    {
        _instances[instance.Id] = instance;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Guid>> GetRunnableInstanceIdsAsync(int batchSize, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        IReadOnlyList<Guid> ids = _instances.Values
            .Where(i => i.Status == WorkflowStatus.Runnable &&
                        (i.NextExecutionTime == null || i.NextExecutionTime <= now))
            .Take(batchSize)
            .Select(i => i.Id)
            .ToList();
        return Task.FromResult(ids);
    }

    public Task PublishEventAsync(WorkflowEvent @event, CancellationToken ct = default)
    {
        _events[@event.Id] = @event;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<WorkflowEvent>> ConsumeEventsAsync(
        string eventName, string eventKey, CancellationToken ct = default)
    {
        IReadOnlyList<WorkflowEvent> result = _events.Values
            .Where(e => e.EventName == eventName && e.EventKey == eventKey && !e.IsConsumed)
            .ToList();

        foreach (var e in result)
            e.IsConsumed = true;

        return Task.FromResult(result);
    }
}
