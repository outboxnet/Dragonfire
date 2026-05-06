using Dragonfire.Inbox.Models;

namespace Dragonfire.WebhookPlatform.SampleApp.Models;

public sealed class IncomingListViewModel
{
    public EventFilter Filter { get; init; } = new();
    public IReadOnlyList<InboxMessage> Messages { get; init; } = [];
    public IReadOnlyDictionary<Guid, IReadOnlyList<InboxHandlerAttempt>> AttemptsByMessage { get; init; }
        = new Dictionary<Guid, IReadOnlyList<InboxHandlerAttempt>>();
    public IReadOnlyList<string> KnownEventTypes { get; init; } = [];
}
