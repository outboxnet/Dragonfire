using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SagaNet.Core.Abstractions;
using SagaNet.Core.Diagnostics;
using SagaNet.Core.Models;

namespace SagaNet.Core.Middleware;

/// <summary>
/// Middleware that records structured logs, distributed traces (OpenTelemetry),
/// and metrics for every step execution.
/// </summary>
public sealed class ObservabilityMiddleware : IWorkflowMiddleware
{
    private readonly ILogger<ObservabilityMiddleware> _logger;

    public ObservabilityMiddleware(ILogger<ObservabilityMiddleware> logger)
        => _logger = logger;

    public async Task<ExecutionResult> InvokeAsync(
        MiddlewareContext context,
        Func<Task<ExecutionResult>> next)
    {
        using var activity = SagaNetDiagnostics.StartStepActivity(
            context.WorkflowName,
            context.StepName,
            context.WorkflowInstanceId);

        var sw = Stopwatch.StartNew();

        _logger.LogDebug(
            "Executing step {StepName} (attempt {Attempt}) for workflow {WorkflowName} [{InstanceId}]",
            context.StepName,
            context.AttemptCount + 1,
            context.WorkflowName,
            context.WorkflowInstanceId);

        SagaNetDiagnostics.StepsExecuted.Add(1,
            new TagList
            {
                { "workflow", context.WorkflowName },
                { "step", context.StepName }
            });

        try
        {
            var result = await next();
            sw.Stop();

            SagaNetDiagnostics.StepDuration.Record(
                sw.Elapsed.TotalMilliseconds,
                new TagList
                {
                    { "workflow", context.WorkflowName },
                    { "step", context.StepName },
                    { "outcome", result.GetType().Name }
                });

            switch (result)
            {
                case ExecutionResult.FailResult fail:
                    _logger.LogWarning(
                        "Step {StepName} failed: {Reason} [{InstanceId}]",
                        context.StepName, fail.Reason, context.WorkflowInstanceId);
                    activity?.SetStatus(ActivityStatusCode.Error, fail.Reason);
                    SagaNetDiagnostics.StepsFailed.Add(1,
                        new TagList { { "workflow", context.WorkflowName }, { "step", context.StepName } });
                    break;

                case ExecutionResult.RetryResult retry:
                    _logger.LogInformation(
                        "Step {StepName} scheduled for retry in {Delay} (attempt {Attempt}) [{InstanceId}]",
                        context.StepName, retry.Delay, context.AttemptCount + 1, context.WorkflowInstanceId);
                    SagaNetDiagnostics.StepsRetried.Add(1,
                        new TagList { { "workflow", context.WorkflowName }, { "step", context.StepName } });
                    break;

                default:
                    _logger.LogDebug(
                        "Step {StepName} completed in {ElapsedMs:F1} ms [{InstanceId}]",
                        context.StepName, sw.Elapsed.TotalMilliseconds, context.WorkflowInstanceId);
                    break;
            }

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddEvent(new ActivityEvent("exception",
                tags: new ActivityTagsCollection
                {
                    ["exception.type"] = ex.GetType().FullName,
                    ["exception.message"] = ex.Message
                }));

            _logger.LogError(ex,
                "Unhandled exception in step {StepName} [{InstanceId}]",
                context.StepName, context.WorkflowInstanceId);

            throw;
        }
    }
}
