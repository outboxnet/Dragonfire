using SagaNet.Core.Abstractions;
using SagaNet.Core.Models;

namespace SagaNet.Core.Builder;

/// <inheritdoc cref="IWorkflowBuilder{TData}"/>
public sealed class WorkflowBuilder<TData> : IWorkflowBuilder<TData>
    where TData : class, new()
{
    private readonly List<MutableStepDefinition> _steps = [];

    public IStepBuilder<TData, TStep> StartWith<TStep>()
        where TStep : IWorkflowStep<TData>
        => StartWith<TStep>(typeof(TStep).Name);

    public IStepBuilder<TData, TStep> StartWith<TStep>(string name)
        where TStep : IWorkflowStep<TData>
    {
        var step = new MutableStepDefinition(0, name, typeof(TStep));
        _steps.Add(step);
        return new StepBuilder<TData, TStep>(step, this);
    }

    /// <summary>
    /// Adds a step in the forward flow after <paramref name="predecessor"/>.
    /// Called by StepBuilder.Then() — links predecessor → new step.
    /// </summary>
    internal IStepBuilder<TData, TNextStep> AddForwardStep<TNextStep>(
        MutableStepDefinition predecessor,
        string name)
        where TNextStep : IWorkflowStep<TData>
    {
        var index = _steps.Count;
        var step = new MutableStepDefinition(index, name, typeof(TNextStep));
        predecessor.NextStepIndices.Add(index);
        _steps.Add(step);
        return new StepBuilder<TData, TNextStep>(step, this);
    }

    /// <summary>
    /// Adds a compensation-only step (not wired into the forward flow).
    /// Returns its index.
    /// </summary>
    internal int AddCompensationStep<TCompensate>(string name)
        where TCompensate : IWorkflowStep<TData>
    {
        var index = _steps.Count;
        var step = new MutableStepDefinition(index, name, typeof(TCompensate))
        {
            IsCompensationStep = true
        };
        _steps.Add(step);
        return index;
    }

    public WorkflowDefinition Build(string workflowName, int version, Type dataType)
    {
        var steps = _steps.Select(s => new StepDefinition
        {
            Index = s.Index,
            Name = s.Name,
            StepType = s.StepType,
            IsCompensatable = typeof(ICompensatableStep<TData>).IsAssignableFrom(s.StepType),
            IsCompensationStep = s.IsCompensationStep,
            CompensateWithStepIndex = s.CompensateWithStepIndex,
            RetryPolicy = s.RetryPolicy,
            NextStepIndices = s.NextStepIndices.AsReadOnly(),
            Description = s.Description
        }).ToList();

        return new WorkflowDefinition
        {
            Name = workflowName,
            Version = version,
            DataType = dataType,
            Steps = steps.AsReadOnly()
        };
    }

    public sealed class MutableStepDefinition(int index, string name, Type stepType)
    {
        public int Index { get; } = index;
        public string Name { get; } = name;
        public Type StepType { get; } = stepType;
        public List<int> NextStepIndices { get; } = [];
        public int? CompensateWithStepIndex { get; set; }
        public RetryPolicy? RetryPolicy { get; set; }
        public string? Description { get; set; }
        public bool IsCompensationStep { get; set; }
    }
}
