namespace OutboxNet.Interfaces;

/// <summary>
/// Provides ambient context values (TenantId, UserId) that are automatically applied
/// to outbox messages when they are published, without requiring callers to pass them explicitly.
/// </summary>
public interface IOutboxContextAccessor
{
    string? TenantId { get; }
    string? UserId { get; }
}
