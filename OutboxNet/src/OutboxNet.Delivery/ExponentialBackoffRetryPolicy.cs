using Microsoft.Extensions.Options;
using OutboxNet.Interfaces;
using OutboxNet.Options;

namespace OutboxNet.Delivery;

public sealed class ExponentialBackoffRetryPolicy : IRetryPolicy
{
    private readonly RetryPolicyOptions _options;

    public ExponentialBackoffRetryPolicy(IOptions<RetryPolicyOptions> options)
    {
        _options = options.Value;
    }

    public bool ShouldRetry(int retryCount) => retryCount < _options.MaxRetries;

    public TimeSpan? GetNextDelay(int retryCount)
    {
        if (!ShouldRetry(retryCount))
            return null;

        var baseDelayMs = _options.BaseDelay.TotalMilliseconds * Math.Pow(2, retryCount);
        var cappedDelayMs = Math.Min(baseDelayMs, _options.MaxDelay.TotalMilliseconds);

        var jitter = cappedDelayMs * _options.JitterFactor * (Random.Shared.NextDouble() * 2 - 1);
        var finalDelayMs = Math.Max(0, cappedDelayMs + jitter);

        return TimeSpan.FromMilliseconds(finalDelayMs);
    }
}
