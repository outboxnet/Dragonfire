using SagaNet.Core.Models;

namespace SagaNet.Core.Abstractions;

/// <summary>
/// Read-side service for querying workflow task progress.
/// Inject this into your API controllers or minimal-API handlers to expose
/// progress data to a frontend.
/// </summary>
public interface IWorkflowQueryService
{
    /// <summary>
    /// Returns a progress snapshot for the given workflow instance ID,
    /// or null if the instance does not exist.
    /// </summary>
    Task<WorkflowProgress?> GetProgressAsync(
        Guid instanceId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns progress snapshots for all workflow instances that share
    /// the given correlation ID (e.g. an external order ID).
    /// </summary>
    Task<IReadOnlyList<WorkflowProgress>> GetProgressByCorrelationIdAsync(
        string correlationId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the most recent workflow instances, newest first.
    /// Useful for admin dashboards.
    /// </summary>
    Task<IReadOnlyList<WorkflowProgress>> GetRecentAsync(
        int limit = 50,
        CancellationToken ct = default);

    /// <summary>
    /// Returns all workflow instances where the named step is currently active —
    /// i.e. its execution pointer status is Pending, Running, or WaitingForEvent.
    /// <para>
    /// Use this to answer: "which orders are currently stuck waiting for payment?"
    /// </para>
    /// </summary>
    /// <param name="stepName">
    /// The step name as declared in the workflow builder,
    /// e.g. <c>"ProcessPayment"</c>.
    /// </param>
    /// <param name="filter">Optional extra filters (workflow type, time window, limit).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<WorkflowProgress>> GetStuckAtStepAsync(
        string stepName,
        StepQueryFilter? filter = null,
        CancellationToken ct = default);

    /// <summary>
    /// Returns all workflow instances where the named step has permanently failed
    /// — i.e. its execution pointer status is Failed.
    /// <para>
    /// Use this to answer: "which orders failed at the payment step today?"
    /// </para>
    /// </summary>
    /// <param name="stepName">
    /// The step name as declared in the workflow builder,
    /// e.g. <c>"ProcessPayment"</c>.
    /// </param>
    /// <param name="filter">Optional extra filters (workflow type, time window, limit).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<WorkflowProgress>> GetFailedAtStepAsync(
        string stepName,
        StepQueryFilter? filter = null,
        CancellationToken ct = default);
}
