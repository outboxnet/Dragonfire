namespace SagaNet.Core.Models;

/// <summary>
/// Runtime state of a single workflow execution.
/// </summary>
public sealed class WorkflowInstance
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Matches the <see cref="Abstractions.IWorkflow{TData}.Name"/>.</summary>
    public string WorkflowName { get; set; } = string.Empty;

    public int Version { get; set; } = 1;

    /// <summary>CLR type name of the data payload, used for deserialization.</summary>
    public string DataType { get; set; } = string.Empty;

    /// <summary>JSON-serialized workflow data payload.</summary>
    public string DataJson { get; set; } = "{}";

    public WorkflowStatus Status { get; set; } = WorkflowStatus.Runnable;

    /// <summary>Ordered list of execution pointers (one per step invocation).</summary>
    public List<ExecutionPointer> ExecutionPointers { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompleteAt { get; set; }
    public DateTime? NextExecutionTime { get; set; }

    /// <summary>Correlation ID for distributed tracing integration.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>Terminal error message, set when status is Failed.</summary>
    public string? Error { get; set; }

    /// <summary>Optimistic concurrency token — prevents double execution across hosts.</summary>
    public byte[] RowVersion { get; set; } = [];

    // ── Convenience helpers ──────────────────────────────────────────────────

    public ExecutionPointer? ActivePointer =>
        ExecutionPointers.FirstOrDefault(p =>
            p.Status is StepStatus.Running or StepStatus.Pending or StepStatus.WaitingForEvent);

    public bool IsTerminal =>
        Status is WorkflowStatus.Complete
                or WorkflowStatus.Compensated
                or WorkflowStatus.Failed
                or WorkflowStatus.Terminated;
}
