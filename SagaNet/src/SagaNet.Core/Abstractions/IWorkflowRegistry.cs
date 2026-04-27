using SagaNet.Core.Models;

namespace SagaNet.Core.Abstractions;

/// <summary>
/// Registry that holds compiled <see cref="WorkflowDefinition"/> objects,
/// keyed by workflow name and version.
/// </summary>
public interface IWorkflowRegistry
{
    /// <summary>Registers a workflow definition.</summary>
    void Register<TWorkflow, TData>()
        where TWorkflow : IWorkflow<TData>
        where TData : class, new();

    /// <summary>Looks up a workflow definition by name and version.</summary>
    WorkflowDefinition? Find(string name, int version);

    /// <summary>Returns all registered definitions.</summary>
    IEnumerable<WorkflowDefinition> GetAll();
}
