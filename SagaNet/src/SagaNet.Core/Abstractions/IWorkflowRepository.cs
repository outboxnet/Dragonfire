using SagaNet.Core.Models;

namespace SagaNet.Core.Abstractions;

/// <summary>
/// Persistence contract for workflow instances and execution pointers.
/// </summary>
public interface IWorkflowRepository
{
    /// <summary>Creates a new workflow instance and returns it with its assigned ID.</summary>
    Task<WorkflowInstance> CreateInstanceAsync(WorkflowInstance instance, CancellationToken ct = default);

    /// <summary>Gets a workflow instance by ID. Returns null if not found.</summary>
    Task<WorkflowInstance?> GetInstanceAsync(Guid instanceId, CancellationToken ct = default);

    /// <summary>
    /// Atomically acquires a workflow instance for execution (sets status to Running
    /// using optimistic concurrency). Returns null if the instance was claimed by another host.
    /// </summary>
    Task<WorkflowInstance?> TryAcquireInstanceAsync(Guid instanceId, CancellationToken ct = default);

    /// <summary>Persists changes to an existing workflow instance.</summary>
    Task UpdateInstanceAsync(WorkflowInstance instance, CancellationToken ct = default);

    /// <summary>
    /// Returns a batch of runnable workflow instance IDs (status = Runnable,
    /// NextExecutionTime &lt;= now) up to <paramref name="batchSize"/>.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetRunnableInstanceIdsAsync(int batchSize, CancellationToken ct = default);

    /// <summary>Stores a publishable event for waiting workflows to consume.</summary>
    Task PublishEventAsync(WorkflowEvent @event, CancellationToken ct = default);

    /// <summary>
    /// Retrieves and marks unconsumed events matching name+key. Marks them consumed atomically.
    /// </summary>
    Task<IReadOnlyList<WorkflowEvent>> ConsumeEventsAsync(
        string eventName,
        string eventKey,
        CancellationToken ct = default);
}
