using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace SagaNet.Core.Diagnostics;

/// <summary>
/// OpenTelemetry instrumentation for SagaNet.
/// Exposes an <see cref="ActivitySource"/> for distributed tracing and a
/// <see cref="Meter"/> for metrics.
/// </summary>
public static class SagaNetDiagnostics
{
    public const string ActivitySourceName = "SagaNet";
    public const string MeterName = "SagaNet";

    public static readonly ActivitySource ActivitySource =
        new(ActivitySourceName, "1.0.0");

    private static readonly Meter _meter = new(MeterName, "1.0.0");

    // ── Counters ─────────────────────────────────────────────────────────────

    public static readonly Counter<long> WorkflowsStarted =
        _meter.CreateCounter<long>("saganet.workflows.started",
            description: "Total number of workflow instances started.");

    public static readonly Counter<long> WorkflowsCompleted =
        _meter.CreateCounter<long>("saganet.workflows.completed",
            description: "Total number of workflow instances completed successfully.");

    public static readonly Counter<long> WorkflowsFailed =
        _meter.CreateCounter<long>("saganet.workflows.failed",
            description: "Total number of workflow instances that failed permanently.");

    public static readonly Counter<long> WorkflowsCompensated =
        _meter.CreateCounter<long>("saganet.workflows.compensated",
            description: "Total number of workflow instances that triggered saga compensation.");

    public static readonly Counter<long> StepsExecuted =
        _meter.CreateCounter<long>("saganet.steps.executed",
            description: "Total step executions (all attempts).");

    public static readonly Counter<long> StepsRetried =
        _meter.CreateCounter<long>("saganet.steps.retried",
            description: "Total step retry attempts.");

    public static readonly Counter<long> StepsFailed =
        _meter.CreateCounter<long>("saganet.steps.failed",
            description: "Total permanently failed steps.");

    // ── Histograms ────────────────────────────────────────────────────────────

    public static readonly Histogram<double> StepDuration =
        _meter.CreateHistogram<double>("saganet.steps.duration_ms",
            unit: "ms",
            description: "Step execution duration in milliseconds.");

    public static readonly Histogram<double> WorkflowDuration =
        _meter.CreateHistogram<double>("saganet.workflows.duration_ms",
            unit: "ms",
            description: "End-to-end workflow duration in milliseconds.");

    // ── Gauges ────────────────────────────────────────────────────────────────

    public static readonly UpDownCounter<long> ActiveWorkflows =
        _meter.CreateUpDownCounter<long>("saganet.workflows.active",
            description: "Number of workflow instances currently running.");

    // ── Activity helpers ──────────────────────────────────────────────────────

    public static Activity? StartWorkflowActivity(string workflowName, Guid instanceId)
    {
        var activity = ActivitySource.StartActivity(
            $"workflow.{workflowName}",
            ActivityKind.Internal);

        activity?.SetTag("saganet.workflow.name", workflowName);
        activity?.SetTag("saganet.workflow.instance_id", instanceId.ToString());
        return activity;
    }

    public static Activity? StartStepActivity(string workflowName, string stepName, Guid instanceId)
    {
        var activity = ActivitySource.StartActivity(
            $"step.{stepName}",
            ActivityKind.Internal);

        activity?.SetTag("saganet.workflow.name", workflowName);
        activity?.SetTag("saganet.step.name", stepName);
        activity?.SetTag("saganet.workflow.instance_id", instanceId.ToString());
        return activity;
    }
}
