using System.Collections.Concurrent;

namespace InboxNet.Dispatch;

internal sealed class InboxHandlerRegistry : IInboxHandlerRegistry
{
    private readonly IReadOnlyList<InboxHandlerRegistration> _registrations;

    // Wildcard registrations (null ProviderKey or EventType) make pure precomputation
    // unbounded, so we cache results lazily. Keyed by case-insensitive (provider, event);
    // the empty list is shared for misses so common no-handler paths are alloc-free.
    private readonly ConcurrentDictionary<MatchKey, IReadOnlyList<InboxHandlerRegistration>> _cache;
    private static readonly IReadOnlyList<InboxHandlerRegistration> Empty = Array.Empty<InboxHandlerRegistration>();

    public InboxHandlerRegistry(IEnumerable<InboxHandlerRegistration> registrations)
    {
        _registrations = registrations
            .OrderBy(r => r.Order)
            .ToList();
        _cache = new ConcurrentDictionary<MatchKey, IReadOnlyList<InboxHandlerRegistration>>();
    }

    public IReadOnlyList<InboxHandlerRegistration> All => _registrations;

    public IReadOnlyList<InboxHandlerRegistration> GetMatching(string providerKey, string eventType)
    {
        var key = new MatchKey(providerKey, eventType);
        return _cache.GetOrAdd(key, static (k, regs) =>
        {
            List<InboxHandlerRegistration>? matches = null;
            foreach (var reg in regs)
            {
                if (!reg.Matches(k.ProviderKey, k.EventType)) continue;
                matches ??= new List<InboxHandlerRegistration>();
                matches.Add(reg);
            }
            return matches is null ? Empty : matches;
        }, _registrations);
    }

    private readonly record struct MatchKey(string ProviderKey, string EventType)
    {
        public bool Equals(MatchKey other) =>
            string.Equals(ProviderKey, other.ProviderKey, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(EventType, other.EventType, StringComparison.OrdinalIgnoreCase);

        public override int GetHashCode() => HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(ProviderKey),
            StringComparer.OrdinalIgnoreCase.GetHashCode(EventType));
    }
}
