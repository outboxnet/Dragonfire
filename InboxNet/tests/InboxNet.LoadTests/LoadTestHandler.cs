using InboxNet.Interfaces;
using InboxNet.Models;

namespace InboxNet.LoadTests;

/// <summary>
/// Inbox handler under test. Records completion timing in <see cref="LoadTestTracker"/> and,
/// on first attempt, may throw to exercise the retry pipeline.
/// </summary>
public sealed class LoadTestHandler : IInboxHandler
{
    private readonly LoadTestTracker _tracker;

    public LoadTestHandler(LoadTestTracker tracker)
    {
        _tracker = tracker;
    }

    public Task HandleAsync(InboxMessage message, CancellationToken ct)
    {
        // ProviderEventId is set from the X-Event-Id header on the publisher side and is
        // also used as the dedup key. RetryCount is 0 on first dispatch attempt.
        var eventId = message.ProviderEventId ?? message.DedupKey;

        if (_tracker.ShouldFail(eventId, message.RetryCount))
        {
            throw new InvalidOperationException($"Injected failure for {eventId}");
        }

        _tracker.MarkHandled(eventId);
        return Task.CompletedTask;
    }
}
