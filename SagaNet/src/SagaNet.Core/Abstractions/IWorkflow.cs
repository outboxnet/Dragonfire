using SagaNet.Core.Builder;

namespace SagaNet.Core.Abstractions;

/// <summary>
/// Defines a workflow with a specific data type.
/// Implement this interface to declare the steps and flow of a workflow.
/// </summary>
/// <typeparam name="TData">The strongly-typed data payload carried through this workflow.</typeparam>
public interface IWorkflow<TData> where TData : class, new()
{
    /// <summary>Gets the unique name identifying this workflow definition.</summary>
    string Name { get; }

    /// <summary>Gets the version of this workflow definition. Defaults to 1.</summary>
    int Version => 1;

    /// <summary>
    /// Builds the workflow step graph using the provided builder.
    /// </summary>
    void Build(IWorkflowBuilder<TData> builder);
}
