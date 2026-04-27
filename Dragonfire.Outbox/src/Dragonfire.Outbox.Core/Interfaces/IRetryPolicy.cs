namespace Dragonfire.Outbox.Interfaces;

public interface IRetryPolicy
{
    TimeSpan? GetNextDelay(int retryCount);
    bool ShouldRetry(int retryCount);
}
