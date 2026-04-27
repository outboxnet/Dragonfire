namespace SagaNet.Core.Models;

/// <summary>
/// Tracks the execution state of a single step within a workflow instance.
/// One pointer exists per step invocation.
/// </summary>
public sealed class ExecutionPointer
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Zero-based index of the step within the workflow definition.</summary>
    public int StepIndex { get; set; }

    public string StepName { get; set; } = string.Empty;

    public StepStatus Status { get; set; } = StepStatus.Pending;

    /// <summary>Number of times this step has been attempted.</summary>
    public int AttemptCount { get; set; }

    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// When retrying, the pointer will not run before this time.
    /// </summary>
    public DateTime? RetryAfter { get; set; }

    /// <summary>For event-waiting steps: the event name to wait for.</summary>
    public string? EventName { get; set; }

    /// <summary>For event-waiting steps: the correlation key.</summary>
    public string? EventKey { get; set; }

    /// <summary>Serialized event data received for this pointer.</summary>
    public string? EventData { get; set; }

    /// <summary>Human-readable error message from the last failed attempt.</summary>
    public string? Error { get; set; }

    /// <summary>Step-scoped key/value bag persisted alongside the instance data.</summary>
    public Dictionary<string, string> PersistenceData { get; set; } = [];
}
