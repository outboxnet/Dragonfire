namespace SagaNet.Persistence.EfCore.Entities;

/// <summary>Database entity for an execution pointer (step invocation).</summary>
public sealed class ExecutionPointerEntity
{
    public Guid Id { get; set; }
    public Guid WorkflowInstanceId { get; set; }
    public int StepIndex { get; set; }
    public string StepName { get; set; } = string.Empty;
    public int Status { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public DateTime? RetryAfter { get; set; }
    public string? EventName { get; set; }
    public string? EventKey { get; set; }
    public string? EventData { get; set; }
    public string? Error { get; set; }

    /// <summary>JSON-serialized Dictionary&lt;string, string&gt; for step-scoped persistence.</summary>
    public string PersistenceDataJson { get; set; } = "{}";

    public WorkflowInstanceEntity WorkflowInstance { get; set; } = null!;
}
