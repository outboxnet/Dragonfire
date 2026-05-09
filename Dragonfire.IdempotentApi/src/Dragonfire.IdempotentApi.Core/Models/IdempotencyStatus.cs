namespace Dragonfire.IdempotentApi.Models;

/// <summary>
/// Lifecycle state of an idempotency entry.
/// </summary>
public enum IdempotencyStatus
{
    /// <summary>The first arriving request has reserved the key but is still executing.</summary>
    Reserved = 0,

    /// <summary>The original request finished and a <see cref="IdempotentResponse"/> has been persisted.</summary>
    Completed = 1,
}
