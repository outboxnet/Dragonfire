namespace SagaNet.Core.Models;

/// <summary>
/// An external event that can resume a suspended workflow step.
/// </summary>
public sealed class WorkflowEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EventName { get; set; } = string.Empty;
    public string EventKey { get; set; } = string.Empty;
    public string? EventDataJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsConsumed { get; set; }
    public DateTime? ConsumedAt { get; set; }
}
