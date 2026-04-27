namespace SagaNet.Core.Models;

/// <summary>
/// A snapshot of the current progress of a workflow instance.
/// Designed to be returned directly to a frontend API caller.
/// </summary>
public sealed class WorkflowProgress
{
    /// <summary>Unique ID of this workflow instance.</summary>
    public Guid InstanceId { get; init; }

    /// <summary>Name of the workflow definition (e.g. "OrderWorkflow").</summary>
    public string WorkflowName { get; init; } = string.Empty;

    public int Version { get; init; }

    /// <summary>Machine-readable status of the overall workflow.</summary>
    public WorkflowStatus Status { get; init; }

    /// <summary>
    /// Human-readable summary of what is happening right now.
    /// Examples: "Processing payment", "Rolled back — payment failed", "Completed".
    /// </summary>
    public string StatusLabel { get; init; } = string.Empty;

    /// <summary>Overall completion percentage (0–100). Always 100 when <see cref="Status"/> is Complete.</summary>
    public int ProgressPercent { get; init; }

    /// <summary>Number of forward-flow steps (excludes compensation steps).</summary>
    public int TotalSteps { get; init; }

    /// <summary>Number of forward-flow steps that have reached <see cref="StepStatus.Complete"/>.</summary>
    public int CompletedSteps { get; init; }

    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }

    /// <summary>Terminal error message, set when <see cref="Status"/> is Failed or Compensated.</summary>
    public string? Error { get; init; }

    /// <summary>Optional correlation ID set at workflow start for cross-system tracking.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Ordered list of steps. Forward-flow steps are always included.
    /// Compensation steps appear only if they actually ran (during or after rollback).
    /// </summary>
    public IReadOnlyList<StepProgress> Steps { get; init; } = [];
}

/// <summary>Progress snapshot for a single step.</summary>
public sealed class StepProgress
{
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Optional description set via <c>.WithDescription("...")</c> in the workflow builder.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>Machine-readable step status.</summary>
    public StepStatus Status { get; init; }

    /// <summary>Human-readable step status label.</summary>
    public string StatusLabel { get; init; } = string.Empty;

    /// <summary>How many times this step has been attempted (1 on first run, 2 on first retry…).</summary>
    public int AttemptCount { get; init; }

    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }

    /// <summary>Wall-clock duration of the last execution in milliseconds. Null if not yet started.</summary>
    public double? DurationMs { get; init; }

    /// <summary>Error message from the last failed attempt, if any.</summary>
    public string? Error { get; init; }

    /// <summary>True for compensation (rollback) steps — shown only when they actually executed.</summary>
    public bool IsCompensation { get; init; }
}
