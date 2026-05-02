namespace Acme.BillingClient;

/// <summary>
/// Context passed to <see cref="IBillingErrorHandler"/> whenever a call
/// returns a non-success status code or fails at the transport layer. The
/// generated client invokes the handler before returning the failed
/// <see cref="ApiResponse{T}"/> to the caller.
/// </summary>
public sealed class BillingErrorContext
{
    public string OperationName { get; init; } = "";
    public int StatusCode { get; init; }
    public string? RawBody { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyDictionary<string, string> Headers { get; init; }
        = new Dictionary<string, string>();
    public TimeSpan Elapsed { get; init; }
    public Exception? Exception { get; init; }
}

/// <summary>
/// Pluggable global hook invoked whenever a call fails. Register a custom
/// implementation via <c>services.AddSingleton&lt;IBillingErrorHandler, MyHandler&gt;()</c>
/// to centralise logging, metrics, or exception translation.
/// </summary>
public interface IBillingErrorHandler
{
    Task HandleAsync(BillingErrorContext context, CancellationToken cancellationToken = default);
}

/// <summary>Default no-op handler. Registered via <c>TryAddSingleton</c> so user overrides win.</summary>
public sealed class NoOpBillingErrorHandler : IBillingErrorHandler
{
    public Task HandleAsync(BillingErrorContext context, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
