using SagaNet.Core.Models;

namespace SagaNet.Core.Builder;

/// <summary>Fluent builder for <see cref="RetryPolicy"/>.</summary>
public sealed class RetryPolicyBuilder
{
    private int _maxAttempts = 3;
    private TimeSpan _initialDelay = TimeSpan.FromSeconds(5);
    private TimeSpan _maxDelay = TimeSpan.FromMinutes(5);
    private double _backoffMultiplier = 2.0;

    public RetryPolicyBuilder MaxAttempts(int attempts)
    {
        _maxAttempts = attempts;
        return this;
    }

    public RetryPolicyBuilder InitialDelay(TimeSpan delay)
    {
        _initialDelay = delay;
        return this;
    }

    public RetryPolicyBuilder MaxDelay(TimeSpan delay)
    {
        _maxDelay = delay;
        return this;
    }

    public RetryPolicyBuilder ExponentialBackoff(double multiplier = 2.0)
    {
        _backoffMultiplier = multiplier;
        return this;
    }

    public RetryPolicyBuilder NoRetry()
    {
        _maxAttempts = 1;
        return this;
    }

    internal RetryPolicy Build() => new()
    {
        MaxAttempts = _maxAttempts,
        InitialDelay = _initialDelay,
        MaxDelay = _maxDelay,
        BackoffMultiplier = _backoffMultiplier
    };
}
