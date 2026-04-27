using InboxNet.Interfaces;

namespace InboxNet.Providers;

internal sealed class WebhookProviderRegistry : IWebhookProviderRegistry
{
    private readonly Dictionary<string, IWebhookProvider> _providers;

    public WebhookProviderRegistry(IEnumerable<IWebhookProvider> providers)
    {
        _providers = providers.ToDictionary(p => p.Key, StringComparer.OrdinalIgnoreCase);
    }

    public IWebhookProvider? Get(string providerKey) =>
        _providers.TryGetValue(providerKey, out var p) ? p : null;

    public bool Contains(string providerKey) => _providers.ContainsKey(providerKey);

    public IReadOnlyCollection<string> Keys => _providers.Keys;
}
