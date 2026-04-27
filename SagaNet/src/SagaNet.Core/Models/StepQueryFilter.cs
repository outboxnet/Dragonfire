namespace SagaNet.Core.Models;

/// <summary>
/// Optional filter applied to step-level queries such as
/// <c>GetStuckAtStepAsync</c> and <c>GetFailedAtStepAsync</c>.
/// All fields are optional — omitting a field means "no restriction".
/// </summary>
public sealed class StepQueryFilter
{
    /// <summary>
    /// Restrict results to a single workflow type (e.g. "OrderWorkflow").
    /// Null means all workflow types are included.
    /// </summary>
    public string? WorkflowName { get; init; }

    /// <summary>
    /// Only include instances created at or after this UTC timestamp.
    /// Useful for time-windowed dashboards: "failed in the last hour".
    /// </summary>
    public DateTime? CreatedAfter { get; init; }

    /// <summary>
    /// Only include instances created before this UTC timestamp.
    /// Combine with <see cref="CreatedAfter"/> to get a specific range.
    /// </summary>
    public DateTime? CreatedBefore { get; init; }

    /// <summary>Maximum number of results returned. Defaults to 50.</summary>
    public int Limit { get; init; } = 50;
}
