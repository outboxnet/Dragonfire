namespace InboxNet.Models;

public class InboxHandlerAttempt
{
    public Guid Id { get; set; }
    public Guid InboxMessageId { get; set; }

    /// <summary>
    /// Stable key identifying the handler across deployments. Defaults to the
    /// handler's fully-qualified type name. Treated as an opaque string by the store.
    /// </summary>
    public string HandlerName { get; set; } = default!;

    public int AttemptNumber { get; set; }
    public InboxHandlerStatus Status { get; set; }
    public long DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset AttemptedAt { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }

    public InboxMessage InboxMessage { get; set; } = default!;
}
