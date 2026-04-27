using SagaNet.Core.Abstractions;

namespace SagaNet.Core.Models;

/// <summary>
/// Compiled, immutable definition of a single workflow step.
/// </summary>
public sealed class StepDefinition
{
    public int Index { get; init; }
    public string Name { get; init; } = string.Empty;

    /// <summary>The CLR type that implements <see cref="IWorkflowStep{TData}"/>.</summary>
    public Type StepType { get; init; } = typeof(object);

    /// <summary>True if the step also implements <see cref="ICompensatableStep{TData}"/>.</summary>
    public bool IsCompensatable { get; init; }

    /// <summary>
    /// Index of the compensation step to run if this step fails.
    /// Null means no explicit compensation (use <see cref="IsCompensatable"/> instead).
    /// </summary>
    public int? CompensateWithStepIndex { get; init; }

    /// <summary>Retry policy attached to this step (overrides global options).</summary>
    public RetryPolicy? RetryPolicy { get; init; }

    /// <summary>Indices of subsequent steps (supports branching).</summary>
    public IReadOnlyList<int> NextStepIndices { get; init; } = [];

    /// <summary>Optional human-readable description.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// True for compensation-only steps (added via <c>CompensateWith&lt;&gt;()</c>).
    /// These are hidden from normal progress display and only shown when actively compensating.
    /// </summary>
    public bool IsCompensationStep { get; init; }
}

/// <summary>Per-step retry configuration.</summary>
public sealed class RetryPolicy
{
    public int MaxAttempts { get; init; } = 3;
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromMinutes(5);
    public double BackoffMultiplier { get; init; } = 2.0;

    public TimeSpan ComputeDelay(int attempt) =>
        TimeSpan.FromMilliseconds(
            Math.Min(
                InitialDelay.TotalMilliseconds * Math.Pow(BackoffMultiplier, attempt - 1),
                MaxDelay.TotalMilliseconds));
}
