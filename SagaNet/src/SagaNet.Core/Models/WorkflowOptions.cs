namespace SagaNet.Core.Models;

/// <summary>
/// Global options for the SagaNet workflow engine.
/// Configure via <c>services.Configure&lt;WorkflowOptions&gt;()</c> or the fluent builder.
/// </summary>
public sealed class WorkflowOptions
{
    public const string SectionName = "SagaNet";

    /// <summary>How often the host polls for runnable workflow instances. Default: 500 ms.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Maximum number of workflow instances processed per poll cycle. Default: 10.</summary>
    public int PollBatchSize { get; set; } = 10;

    /// <summary>
    /// Degree of parallelism for executing workflow instances within a single poll cycle.
    /// Default: number of logical processors, capped at 16.
    /// </summary>
    public int MaxConcurrentWorkflows { get; set; } =
        Math.Min(Environment.ProcessorCount, 16);

    /// <summary>
    /// Default retry policy applied to all steps that do not specify their own.
    /// </summary>
    public RetryPolicy DefaultRetryPolicy { get; set; } = new();

    /// <summary>
    /// Maximum duration a single step may run before being cancelled. Default: 5 minutes.
    /// </summary>
    public TimeSpan StepTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// When true, completed workflow instances are soft-deleted after <see cref="InstanceRetentionPeriod"/>.
    /// Default: false (retain forever).
    /// </summary>
    public bool EnableInstanceCleanup { get; set; }

    /// <summary>How long completed instances are retained before cleanup. Default: 30 days.</summary>
    public TimeSpan InstanceRetentionPeriod { get; set; } = TimeSpan.FromDays(30);
}
