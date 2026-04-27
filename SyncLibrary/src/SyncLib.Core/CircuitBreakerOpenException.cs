namespace SyncLib.Core;

/// <summary>
/// Thrown internally when a sync run is skipped because its provider's circuit
/// breaker is currently open.
/// </summary>
public sealed class CircuitBreakerOpenException : Exception
{
    /// <inheritdoc />
    public CircuitBreakerOpenException(string message) : base(message) { }

    /// <inheritdoc />
    public CircuitBreakerOpenException(string message, Exception inner) : base(message, inner) { }
}
