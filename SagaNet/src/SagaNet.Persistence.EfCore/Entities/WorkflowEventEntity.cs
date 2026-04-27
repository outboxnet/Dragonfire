namespace SagaNet.Persistence.EfCore.Entities;

/// <summary>Database entity for a published workflow event.</summary>
public sealed class WorkflowEventEntity
{
    public Guid Id { get; set; }
    public string EventName { get; set; } = string.Empty;
    public string EventKey { get; set; } = string.Empty;
    public string? EventDataJson { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsConsumed { get; set; }
    public DateTime? ConsumedAt { get; set; }
}
