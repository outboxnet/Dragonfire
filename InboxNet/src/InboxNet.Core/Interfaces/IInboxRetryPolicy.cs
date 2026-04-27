namespace InboxNet.Interfaces;

public interface IInboxRetryPolicy
{
    /// <summary>Returns null to stop retrying (dead-letter); otherwise the delay until the next attempt.</summary>
    TimeSpan? GetNextDelay(int retryCount);
    bool ShouldRetry(int retryCount);
}
