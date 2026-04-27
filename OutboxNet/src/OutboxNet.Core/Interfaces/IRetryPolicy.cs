namespace OutboxNet.Interfaces;

public interface IRetryPolicy
{
    TimeSpan? GetNextDelay(int retryCount);
    bool ShouldRetry(int retryCount);
}
