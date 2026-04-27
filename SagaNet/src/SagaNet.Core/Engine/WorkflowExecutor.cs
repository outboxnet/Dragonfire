using System.Text.Json;
using Microsoft.Extensions.Logging;
using SagaNet.Core.Abstractions;
using SagaNet.Core.Diagnostics;
using SagaNet.Core.Models;

namespace SagaNet.Core.Engine;

/// <summary>
/// Advances a single workflow instance by one step tick.
/// Called repeatedly by <see cref="WorkflowHost"/> until the workflow reaches a terminal state.
/// </summary>
public sealed class WorkflowExecutor
{
    private readonly IWorkflowRegistry _registry;
    private readonly IWorkflowRepository _repository;
    private readonly StepExecutor _stepExecutor; // internal
    private readonly SagaCompensator _compensator;
    private readonly ILogger<WorkflowExecutor> _logger;

    public WorkflowExecutor(
        IWorkflowRegistry registry,
        IWorkflowRepository repository,
        StepExecutor stepExecutor,
        SagaCompensator compensator,
        ILogger<WorkflowExecutor> logger)
    {
        _registry = registry;
        _repository = repository;
        _stepExecutor = stepExecutor;
        _compensator = compensator;
        _logger = logger;
    }

    /// <summary>
    /// Executes one tick of the given workflow instance.
    /// All mutations are persisted before returning.
    /// </summary>
    public async Task ExecuteAsync(Guid instanceId, CancellationToken ct)
    {
        var instance = await _repository.TryAcquireInstanceAsync(instanceId, ct);
        if (instance is null)
        {
            // Another host claimed it — nothing to do.
            return;
        }

        var definition = _registry.Find(instance.WorkflowName, instance.Version);
        if (definition is null)
        {
            _logger.LogError(
                "No definition found for workflow {WorkflowName} v{Version} [{InstanceId}]. Marking failed.",
                instance.WorkflowName, instance.Version, instanceId);

            instance.Status = WorkflowStatus.Failed;
            instance.Error = $"No definition found for workflow '{instance.WorkflowName}' v{instance.Version}.";
            await _repository.UpdateInstanceAsync(instance, ct);
            return;
        }

        using var activity = SagaNetDiagnostics.StartWorkflowActivity(definition.Name, instance.Id);

        try
        {
            await TickAsync(instance, definition, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unhandled error executing workflow {WorkflowName} [{InstanceId}]",
                definition.Name, instance.Id);

            instance.Status = WorkflowStatus.Failed;
            instance.Error = ex.Message;
            SagaNetDiagnostics.WorkflowsFailed.Add(1,
                new System.Diagnostics.TagList { { "workflow", definition.Name } });
        }
        finally
        {
            await _repository.UpdateInstanceAsync(instance, ct);
        }
    }

    // ────────────────────────────────────────────────────────────────────────

    private async Task TickAsync(
        WorkflowInstance instance,
        WorkflowDefinition definition,
        CancellationToken ct)
    {
        // Initialise execution pointers on first tick.
        if (instance.ExecutionPointers.Count == 0 && definition.Steps.Count > 0)
        {
            var first = definition.Steps[0];
            instance.ExecutionPointers.Add(new ExecutionPointer
            {
                StepIndex = first.Index,
                StepName = first.Name,
                Status = StepStatus.Pending
            });
        }

        var pointer = instance.ExecutionPointers
            .FirstOrDefault(p => p.Status is StepStatus.Pending && IsReady(p));

        if (pointer is null)
        {
            // Check whether all forward-flow steps are complete.
            if (IsComplete(instance, definition))
            {
                instance.Status = WorkflowStatus.Complete;
                instance.CompleteAt = DateTime.UtcNow;
                SagaNetDiagnostics.WorkflowsCompleted.Add(1,
                    new System.Diagnostics.TagList { { "workflow", definition.Name } });
                SagaNetDiagnostics.ActiveWorkflows.Add(-1,
                    new System.Diagnostics.TagList { { "workflow", definition.Name } });
                return;
            }

            // Still waiting — keep Runnable to be polled again.
            instance.Status = WorkflowStatus.Runnable;
            return;
        }

        var stepDef = definition.Steps.First(s => s.Index == pointer.StepIndex);
        pointer.Status = StepStatus.Running;
        pointer.StartTime = DateTime.UtcNow;
        pointer.AttemptCount++;

        // Invoke the generic execution method via reflection to handle the generic TData type.
        var result = await ExecuteStepAsync(instance, definition, stepDef, pointer, ct);

        await ApplyResultAsync(result, instance, definition, stepDef, pointer, ct);
    }

    private async Task<ExecutionResult> ExecuteStepAsync(
        WorkflowInstance instance,
        WorkflowDefinition definition,
        StepDefinition stepDef,
        ExecutionPointer pointer,
        CancellationToken ct)
    {
        // Call StepExecutor.ExecuteAsync<TData> reflectively so we preserve strong typing.
        var method = typeof(StepExecutor)
            .GetMethod(nameof(StepExecutor.ExecuteAsync))!
            .MakeGenericMethod(definition.DataType);

        var task = (Task<ExecutionResult>)method.Invoke(
            _stepExecutor,
            [instance, definition, stepDef, pointer, ct])!;

        return await task;
    }

    private async Task ApplyResultAsync(
        ExecutionResult result,
        WorkflowInstance instance,
        WorkflowDefinition definition,
        StepDefinition stepDef,
        ExecutionPointer pointer,
        CancellationToken ct)
    {
        switch (result)
        {
            case ExecutionResult.NextResult next:
                pointer.Status = StepStatus.Complete;
                pointer.EndTime = DateTime.UtcNow;

                // Queue successor steps (skip if a pointer already exists — defensive
                // guard against any scenario that would produce duplicate step indices).
                foreach (var nextIndex in stepDef.NextStepIndices)
                {
                    if (instance.ExecutionPointers.Any(p => p.StepIndex == nextIndex))
                        continue;

                    var nextDef = definition.Steps.First(s => s.Index == nextIndex);
                    instance.ExecutionPointers.Add(new ExecutionPointer
                    {
                        StepIndex = nextDef.Index,
                        StepName = nextDef.Name,
                        Status = StepStatus.Pending,
                        RetryAfter = next.Delay.HasValue
                            ? DateTime.UtcNow + next.Delay.Value
                            : null
                    });
                }

                instance.Status = WorkflowStatus.Runnable;
                break;

            case ExecutionResult.CompleteResult:
                pointer.Status = StepStatus.Complete;
                pointer.EndTime = DateTime.UtcNow;
                instance.Status = WorkflowStatus.Complete;
                instance.CompleteAt = DateTime.UtcNow;
                SagaNetDiagnostics.WorkflowsCompleted.Add(1,
                    new System.Diagnostics.TagList { { "workflow", definition.Name } });
                SagaNetDiagnostics.ActiveWorkflows.Add(-1,
                    new System.Diagnostics.TagList { { "workflow", definition.Name } });
                break;

            case ExecutionResult.RetryResult retry:
                pointer.Status = StepStatus.Pending;
                pointer.RetryAfter = DateTime.UtcNow + retry.Delay;
                pointer.Error = retry.Reason;
                instance.Status = WorkflowStatus.Runnable;
                instance.NextExecutionTime = pointer.RetryAfter;
                break;

            case ExecutionResult.WaitForEventResult wait:
                pointer.Status = StepStatus.WaitingForEvent;
                pointer.EventName = wait.EventName;
                pointer.EventKey = wait.EventKey;
                instance.Status = WorkflowStatus.Suspended;

                // Check if the event was already published (race condition guard).
                var events = await _repository.ConsumeEventsAsync(wait.EventName, wait.EventKey, ct);
                if (events.Count > 0)
                {
                    pointer.EventData = events[0].EventDataJson;
                    pointer.Status = StepStatus.Pending;
                    instance.Status = WorkflowStatus.Runnable;
                }
                break;

            case ExecutionResult.FailResult fail:
                pointer.Status = StepStatus.Failed;
                pointer.Error = fail.Reason;
                pointer.EndTime = DateTime.UtcNow;

                // Trigger saga compensation.
                bool allCompensated = await CompensateAsync(instance, definition, ct);
                instance.Status = allCompensated ? WorkflowStatus.Compensated : WorkflowStatus.Failed;
                instance.Error = fail.Reason;
                instance.CompleteAt = DateTime.UtcNow;

                if (allCompensated)
                    SagaNetDiagnostics.WorkflowsCompensated.Add(1,
                        new System.Diagnostics.TagList { { "workflow", definition.Name } });
                else
                    SagaNetDiagnostics.WorkflowsFailed.Add(1,
                        new System.Diagnostics.TagList { { "workflow", definition.Name } });

                SagaNetDiagnostics.ActiveWorkflows.Add(-1,
                    new System.Diagnostics.TagList { { "workflow", definition.Name } });
                break;
        }
    }

    private async Task<bool> CompensateAsync(
        WorkflowInstance instance,
        WorkflowDefinition definition,
        CancellationToken ct)
    {
        // Use reflection to call the generic CompensateAsync<TData>.
        var method = typeof(SagaCompensator)
            .GetMethod(nameof(SagaCompensator.CompensateAsync))!
            .MakeGenericMethod(definition.DataType);

        var task = (Task<bool>)method.Invoke(_compensator, [instance, definition, ct])!;
        return await task;
    }

    private static bool IsReady(ExecutionPointer pointer)
        => pointer.RetryAfter is null || pointer.RetryAfter <= DateTime.UtcNow;

    private static bool IsComplete(WorkflowInstance instance, WorkflowDefinition definition)
    {
        var completed = instance.ExecutionPointers
            .Where(p => p.Status == StepStatus.Complete)
            .Select(p => p.StepIndex)
            .ToHashSet();

        // All forward-flow steps (i.e., excluding compensation-only steps) must be complete.
        // A simple heuristic: if every step with no predecessors that is NOT a compensation step
        // has been completed, and no step is still pending/running, the workflow is done.
        bool anyActivePointer = instance.ExecutionPointers
            .Any(p => p.Status is StepStatus.Pending or StepStatus.Running or StepStatus.WaitingForEvent);

        if (anyActivePointer) return false;

        bool anyFailed = instance.ExecutionPointers
            .Any(p => p.Status == StepStatus.Failed);

        return !anyFailed;
    }
}
