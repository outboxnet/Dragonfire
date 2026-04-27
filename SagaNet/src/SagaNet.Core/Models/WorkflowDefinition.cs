namespace SagaNet.Core.Models;

/// <summary>
/// Compiled, immutable definition of a workflow — produced by <see cref="Builder.WorkflowBuilder{TData}"/>.
/// </summary>
public sealed class WorkflowDefinition
{
    public string Name { get; init; } = string.Empty;
    public int Version { get; init; } = 1;
    public Type DataType { get; init; } = typeof(object);
    public IReadOnlyList<StepDefinition> Steps { get; init; } = [];

    /// <summary>Optional description for tooling and diagnostics.</summary>
    public string? Description { get; init; }
}
