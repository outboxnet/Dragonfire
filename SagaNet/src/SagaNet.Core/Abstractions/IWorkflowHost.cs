namespace SagaNet.Core.Abstractions;

/// <summary>
/// The workflow host manages starting, resuming, and publishing events to workflows.
/// </summary>
public interface IWorkflowHost
{
    /// <summary>
    /// Starts a new workflow instance and persists it, ready for execution.
    /// </summary>
    /// <typeparam name="TWorkflow">The workflow definition type.</typeparam>
    /// <typeparam name="TData">The workflow data type.</typeparam>
    /// <param name="data">Initial data for the workflow.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The newly created workflow instance ID.</returns>
    Task<Guid> StartWorkflowAsync<TWorkflow, TData>(
        TData data,
        CancellationToken cancellationToken = default)
        where TWorkflow : IWorkflow<TData>
        where TData : class, new();

    /// <summary>
    /// Publishes an event that can resume a workflow waiting on that event.
    /// </summary>
    Task PublishEventAsync(
        string eventName,
        string eventKey,
        object? eventData = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Terminates a running workflow instance.
    /// </summary>
    Task TerminateWorkflowAsync(Guid instanceId, CancellationToken cancellationToken = default);
}
