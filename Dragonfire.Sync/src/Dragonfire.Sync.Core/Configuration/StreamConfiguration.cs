using Dragonfire.Sync.Abstractions;

namespace Dragonfire.Sync.Core.Configuration;

/// <summary>
/// Per-stream knobs: schedule, retries and circuit-breaker thresholds. Each
/// registered <see cref="SyncStateKey"/> stream owns its own instance.
/// </summary>
public sealed class StreamConfiguration
{
    /// <summary>Interval between scheduled runs when hosted by <see cref="SyncOrchestrator"/>.</summary>
    public TimeSpan SyncInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Number of retry attempts after the initial run fails. Total attempts = MaxRetryAttempts + 1.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Base delay for exponential back-off between retries.</summary>
    public TimeSpan RetryDelayBase { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Whether to apply a per-stream circuit breaker.</summary>
    public bool EnableCircuitBreaker { get; set; } = true;

    /// <summary>Consecutive failures after which the breaker opens.</summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>Time the breaker stays open before allowing one trial run.</summary>
    public TimeSpan CircuitBreakerTimeout { get; set; } = TimeSpan.FromMinutes(5);
}
