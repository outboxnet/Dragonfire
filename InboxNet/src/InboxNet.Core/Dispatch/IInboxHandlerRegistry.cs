namespace InboxNet.Dispatch;

/// <summary>
/// Read-only view over all registered <see cref="InboxHandlerRegistration"/>s.
/// Populated at container-build time by <c>AddHandler&lt;T&gt;</c>.
/// </summary>
public interface IInboxHandlerRegistry
{
    IReadOnlyList<InboxHandlerRegistration> All { get; }

    /// <summary>
    /// Returns all registrations matching <paramref name="providerKey"/> and
    /// <paramref name="eventType"/>, ordered by <see cref="InboxHandlerRegistration.Order"/>.
    /// Empty list if no handlers match.
    /// </summary>
    IReadOnlyList<InboxHandlerRegistration> GetMatching(string providerKey, string eventType);
}
