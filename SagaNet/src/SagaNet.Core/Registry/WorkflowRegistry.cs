using Microsoft.Extensions.DependencyInjection;
using SagaNet.Core.Abstractions;
using SagaNet.Core.Builder;
using SagaNet.Core.Models;

namespace SagaNet.Core.Registry;

/// <inheritdoc cref="IWorkflowRegistry"/>
public sealed class WorkflowRegistry : IWorkflowRegistry
{
    private readonly Dictionary<(string Name, int Version), WorkflowDefinition> _definitions = new();
    private readonly IServiceProvider _serviceProvider;

    public WorkflowRegistry(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public void Register<TWorkflow, TData>()
        where TWorkflow : IWorkflow<TData>
        where TData : class, new()
    {
        var workflow = ActivatorUtilities.CreateInstance<TWorkflow>(_serviceProvider);
        var builder = new WorkflowBuilder<TData>();
        workflow.Build(builder);
        var definition = builder.Build(workflow.Name, workflow.Version, typeof(TData));

        var key = (definition.Name, definition.Version);
        _definitions[key] = definition;
    }

    public WorkflowDefinition? Find(string name, int version)
        => _definitions.TryGetValue((name, version), out var def) ? def : null;

    public IEnumerable<WorkflowDefinition> GetAll() => _definitions.Values;
}
