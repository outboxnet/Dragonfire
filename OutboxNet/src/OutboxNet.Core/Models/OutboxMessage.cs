namespace OutboxNet.Models;

public class OutboxMessage
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = default!;
    public string Payload { get; set; } = default!;
    public string? CorrelationId { get; set; }
    public string? TraceId { get; set; }
    public MessageStatus Status { get; set; }
    public int RetryCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public string? LockedBy { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }
    public string? LastError { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
    public string? TenantId { get; set; }
    public string? UserId { get; set; }
    public string? EntityId { get; set; }
}
