namespace InboxNet.Dispatch;

/// <summary>
/// Metadata describing how a single <see cref="Interfaces.IInboxHandler"/> type is
/// wired into the inbox dispatcher. One registration per <c>AddHandler&lt;T&gt;</c> call.
/// Registrations are ordered by <see cref="Order"/>; when multiple handlers match the
/// same message, they are invoked in ascending order of this value (ties broken by
/// registration order).
/// </summary>
public sealed record InboxHandlerRegistration
{
    /// <summary>Concrete handler type used to resolve the handler from DI.</summary>
    public required Type HandlerType { get; init; }

    /// <summary>
    /// Stable identity for this handler persisted to <c>InboxHandlerAttempts.HandlerName</c>.
    /// Defaults to <see cref="Type.FullName"/>. Keep stable across deployments — changing
    /// this value makes prior success records invisible and causes re-dispatch.
    /// </summary>
    public required string HandlerName { get; init; }

    /// <summary>When set, the handler only fires for messages whose <c>ProviderKey</c> matches.</summary>
    public string? ProviderKey { get; init; }

    /// <summary>When set, the handler only fires for messages whose <c>EventType</c> matches.</summary>
    public string? EventType { get; init; }

    /// <summary>Maximum per-handler attempts before this handler is marked dead-lettered.</summary>
    public int MaxRetries { get; init; } = 5;

    /// <summary>Ascending dispatch order within a single message. Lower runs first.</summary>
    public int Order { get; init; }

    public bool Matches(string providerKey, string eventType) =>
        (ProviderKey is null || string.Equals(ProviderKey, providerKey, StringComparison.OrdinalIgnoreCase)) &&
        (EventType   is null || string.Equals(EventType,   eventType,   StringComparison.OrdinalIgnoreCase));
}
