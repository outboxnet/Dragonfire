using SagaNet.Core.Abstractions;
using SagaNet.Core.Models;

namespace SagaNet.Core.Builder;

/// <inheritdoc cref="IStepBuilder{TData,TStep}"/>
public sealed class StepBuilder<TData, TStep>(
    WorkflowBuilder<TData>.MutableStepDefinition current,
    WorkflowBuilder<TData> parent)
    : IStepBuilder<TData, TStep>
    where TData : class, new()
    where TStep : IWorkflowStep<TData>
{
    public IStepBuilder<TData, TNextStep> Then<TNextStep>()
        where TNextStep : IWorkflowStep<TData>
        => parent.AddForwardStep<TNextStep>(current, typeof(TNextStep).Name);

    public IStepBuilder<TData, TNextStep> Then<TNextStep>(string name)
        where TNextStep : IWorkflowStep<TData>
        => parent.AddForwardStep<TNextStep>(current, name);

    public IStepBuilder<TData, TStep> CompensateWith<TCompensate>()
        where TCompensate : ICompensatableStep<TData>
    {
        var compIndex = parent.AddCompensationStep<TCompensate>(
            $"Compensate:{typeof(TCompensate).Name}");
        current.CompensateWithStepIndex = compIndex;
        return this;
    }

    public IStepBuilder<TData, TStep> WithRetry(RetryPolicy policy)
    {
        current.RetryPolicy = policy;
        return this;
    }

    public IStepBuilder<TData, TStep> WithRetry(Action<RetryPolicyBuilder> configure)
    {
        var builder = new RetryPolicyBuilder();
        configure(builder);
        current.RetryPolicy = builder.Build();
        return this;
    }

    public IStepBuilder<TData, TStep> WithDescription(string description)
    {
        current.Description = description;
        return this;
    }
}
