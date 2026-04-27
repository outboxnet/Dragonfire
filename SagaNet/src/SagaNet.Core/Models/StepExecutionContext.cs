using SagaNet.Core.Abstractions;

namespace SagaNet.Core.Models;

/// <summary>
/// Passed into each step at execution time, providing access to workflow state,
/// the persistence layer, and cancellation.
/// </summary>
/// <typeparam name="TData">The workflow data type.</typeparam>
public sealed class StepExecutionContext<TData> where TData : class, new()
{
    /// <summary>ID of the owning workflow instance.</summary>
    public required Guid WorkflowInstanceId { get; init; }

    /// <summary>Mutable workflow data — changes are persisted after the step completes.</summary>
    public required TData Data { get; init; }

    /// <summary>The execution pointer for this step invocation.</summary>
    public required ExecutionPointer Pointer { get; init; }

    /// <summary>The step definition (name, retry policy, etc.).</summary>
    public required StepDefinition StepDefinition { get; init; }

    /// <summary>How many times this step has previously been attempted (0-based).</summary>
    public int AttemptCount => Pointer.AttemptCount;

    /// <summary>Token that is cancelled when the host is shutting down.</summary>
    public required CancellationToken CancellationToken { get; init; }

    /// <summary>Persistence provider for cross-step data (step-scoped key/value store).</summary>
    public IDictionary<string, string> PersistenceData => Pointer.PersistenceData;
}

/// <summary>Context passed to <see cref="IWorkflowMiddleware"/> implementations.</summary>
public sealed class MiddlewareContext
{
    public required Guid WorkflowInstanceId { get; init; }
    public required string WorkflowName { get; init; }
    public required string StepName { get; init; }
    public required int AttemptCount { get; init; }
    public required CancellationToken CancellationToken { get; init; }
}
