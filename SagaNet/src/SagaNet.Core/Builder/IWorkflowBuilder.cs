using SagaNet.Core.Abstractions;
using SagaNet.Core.Models;

namespace SagaNet.Core.Builder;

/// <summary>
/// Fluent API for composing a workflow's step graph.
/// </summary>
/// <typeparam name="TData">The workflow data type.</typeparam>
public interface IWorkflowBuilder<TData> where TData : class, new()
{
    /// <summary>Adds the first step in the workflow.</summary>
    IStepBuilder<TData, TStep> StartWith<TStep>()
        where TStep : IWorkflowStep<TData>;

    /// <summary>Adds the first step with a descriptive name.</summary>
    IStepBuilder<TData, TStep> StartWith<TStep>(string name)
        where TStep : IWorkflowStep<TData>;

    /// <summary>Builds and returns the final <see cref="WorkflowDefinition"/>.</summary>
    WorkflowDefinition Build(string workflowName, int version, Type dataType);
}

/// <summary>
/// Fluent configuration for an individual workflow step.
/// </summary>
/// <typeparam name="TData">The workflow data type.</typeparam>
/// <typeparam name="TStep">The step implementation type.</typeparam>
public interface IStepBuilder<TData, TStep>
    where TData : class, new()
    where TStep : IWorkflowStep<TData>
{
    /// <summary>Appends the next step in the linear sequence.</summary>
    IStepBuilder<TData, TNextStep> Then<TNextStep>()
        where TNextStep : IWorkflowStep<TData>;

    /// <summary>Appends the next step with a descriptive name.</summary>
    IStepBuilder<TData, TNextStep> Then<TNextStep>(string name)
        where TNextStep : IWorkflowStep<TData>;

    /// <summary>
    /// Specifies a compensating step type to run if this step (or any later step) fails.
    /// The compensating step must implement <see cref="ICompensatableStep{TData}"/>.
    /// </summary>
    IStepBuilder<TData, TStep> CompensateWith<TCompensate>()
        where TCompensate : ICompensatableStep<TData>;

    /// <summary>Attaches a per-step retry policy, overriding the global default.</summary>
    IStepBuilder<TData, TStep> WithRetry(RetryPolicy policy);

    /// <summary>Attaches a per-step retry policy via a builder delegate.</summary>
    IStepBuilder<TData, TStep> WithRetry(Action<RetryPolicyBuilder> configure);

    /// <summary>Adds a human-readable description to the step for tooling/logging.</summary>
    IStepBuilder<TData, TStep> WithDescription(string description);
}
