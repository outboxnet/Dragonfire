namespace SagaNet.Persistence.EfCore.Entities;

/// <summary>Database entity for a workflow instance.</summary>
public sealed class WorkflowInstanceEntity
{
    public Guid Id { get; set; }
    public string WorkflowName { get; set; } = string.Empty;
    public int Version { get; set; }
    public string DataType { get; set; } = string.Empty;
    public string DataJson { get; set; } = "{}";
    public int Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompleteAt { get; set; }
    public DateTime? NextExecutionTime { get; set; }
    public string? CorrelationId { get; set; }
    public string? Error { get; set; }

    /// <summary>EF Core optimistic concurrency token.</summary>
    public byte[] RowVersion { get; set; } = [];

    public List<ExecutionPointerEntity> ExecutionPointers { get; set; } = [];
}
