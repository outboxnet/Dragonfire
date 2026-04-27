namespace InboxNet.Interfaces;

/// <summary>
/// Read-only lookup from provider key to its registered <see cref="IWebhookProvider"/>.
/// Populated at container-build time by <c>AddProvider&lt;T&gt;</c> calls.
/// </summary>
public interface IWebhookProviderRegistry
{
    IWebhookProvider? Get(string providerKey);
    bool Contains(string providerKey);
    IReadOnlyCollection<string> Keys { get; }
}
