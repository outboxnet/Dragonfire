using SagaNet.Core.Models;

namespace SagaNet.Core.Abstractions;

/// <summary>
/// A single executable unit of work within a workflow.
/// </summary>
/// <typeparam name="TData">The workflow data type.</typeparam>
public interface IWorkflowStep<TData> where TData : class, new()
{
    /// <summary>
    /// Executes the step logic. Returns an <see cref="ExecutionResult"/> describing
    /// how the workflow should proceed after this step.
    /// </summary>
    Task<ExecutionResult> RunAsync(StepExecutionContext<TData> context);
}

/// <summary>
/// A workflow step that supports saga compensation (rollback).
/// Implement this when the step performs a side-effect that must be undone on failure.
/// </summary>
/// <typeparam name="TData">The workflow data type.</typeparam>
public interface ICompensatableStep<TData> : IWorkflowStep<TData> where TData : class, new()
{
    /// <summary>
    /// Executes the compensating (rollback) action for this step.
    /// Called automatically when a later step fails and saga compensation is triggered.
    /// </summary>
    Task CompensateAsync(StepExecutionContext<TData> context);
}
