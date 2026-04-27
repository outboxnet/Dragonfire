using Microsoft.Extensions.Options;
using InboxNet.Interfaces;
using InboxNet.Options;

namespace InboxNet.Retry;

public sealed class ExponentialBackoffInboxRetryPolicy : IInboxRetryPolicy
{
    private readonly InboxRetryPolicyOptions _options;

    public ExponentialBackoffInboxRetryPolicy(IOptions<InboxRetryPolicyOptions> options)
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

        // Multiplicative jitter clamped to [0, 1] keeps the result in [0, 2 × cappedDelay]
        // — never negative regardless of the configured factor.
        var jitter = _options.ClampedJitterFactor * (Random.Shared.NextDouble() * 2 - 1);
        var finalDelayMs = cappedDelayMs * (1 + jitter);

        return TimeSpan.FromMilliseconds(finalDelayMs);
    }
}
