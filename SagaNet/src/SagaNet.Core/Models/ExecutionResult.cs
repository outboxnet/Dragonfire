namespace SagaNet.Core.Models;

/// <summary>
/// Discriminated union returned by a workflow step, telling the engine what to do next.
/// Use the static factory methods to create the appropriate result.
/// </summary>
public abstract record ExecutionResult
{
    private ExecutionResult() { }

    // ── Factory methods ──────────────────────────────────────────────────────

    /// <summary>Step succeeded; advance to the next step immediately.</summary>
    public static ExecutionResult Next() => new NextResult();

    /// <summary>Step succeeded; advance to the next step after a delay.</summary>
    public static ExecutionResult Next(TimeSpan delay) => new NextResult { Delay = delay };

    /// <summary>Step should be retried after the specified delay.</summary>
    public static ExecutionResult Retry(TimeSpan delay, string? reason = null)
        => new RetryResult(delay, reason);

    /// <summary>Step failed permanently; trigger saga compensation.</summary>
    public static ExecutionResult Fail(string reason, Exception? exception = null)
        => new FailResult(reason, exception);

    /// <summary>The workflow is complete — used as the terminal result of the last step.</summary>
    public static ExecutionResult Complete() => new CompleteResult();

    /// <summary>
    /// Suspend the workflow until an external event with the given name and key is received.
    /// </summary>
    public static ExecutionResult WaitForEvent(string eventName, string eventKey)
        => new WaitForEventResult(eventName, eventKey);

    // ── Concrete result types ────────────────────────────────────────────────

    public sealed record NextResult : ExecutionResult
    {
        public TimeSpan? Delay { get; init; }
    }

    public sealed record RetryResult(TimeSpan Delay, string? Reason) : ExecutionResult;

    public sealed record FailResult(string Reason, Exception? Exception) : ExecutionResult;

    public sealed record CompleteResult : ExecutionResult;

    public sealed record WaitForEventResult(string EventName, string EventKey) : ExecutionResult;
}
