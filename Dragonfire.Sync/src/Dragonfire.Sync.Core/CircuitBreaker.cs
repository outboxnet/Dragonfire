using Microsoft.Extensions.Logging;

namespace Dragonfire.Sync.Core;

/// <summary>
/// Simple count-based circuit breaker: opens when consecutive failures reach a
/// threshold, then auto-closes after a timeout.
/// </summary>
internal sealed class CircuitBreaker
{
    private readonly int _failureThreshold;
    private readonly TimeSpan _timeout;
    private readonly ILogger _logger;
    private readonly object _lock = new();
    private int _failureCount;
    private DateTime? _openUntilUtc;

    public CircuitBreaker(int failureThreshold, TimeSpan timeout, ILogger logger)
    {
        _failureThreshold = failureThreshold;
        _timeout = timeout;
        _logger = logger;
    }

    public bool IsOpen
    {
        get
        {
            lock (_lock)
            {
                if (!_openUntilUtc.HasValue)
                {
                    return false;
                }
                if (DateTime.UtcNow >= _openUntilUtc.Value)
                {
                    _openUntilUtc = null;
                    _failureCount = 0;
                    _logger.LogInformation("Circuit breaker closed after timeout");
                    return false;
                }
                return true;
            }
        }
    }

    public void RecordSuccess()
    {
        lock (_lock)
        {
            _failureCount = 0;
            _openUntilUtc = null;
        }
    }

    public void RecordFailure()
    {
        lock (_lock)
        {
            _failureCount++;
            if (_failureCount >= _failureThreshold && !_openUntilUtc.HasValue)
            {
                _openUntilUtc = DateTime.UtcNow.Add(_timeout);
                _logger.LogWarning("Circuit breaker opened for {Timeout} due to {FailureCount} failures", _timeout, _failureCount);
            }
        }
    }
}
