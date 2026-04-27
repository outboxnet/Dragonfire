using Microsoft.Extensions.Logging;
using SagaNet.Core.Abstractions;
using SagaNet.Core.Models;

namespace SagaNet.Core.Engine;

/// <summary>
/// Walks backward through completed steps and calls their compensation actions
/// when a saga failure is detected.
/// </summary>
public sealed class SagaCompensator
{
    private readonly StepExecutor _stepExecutor;
    private readonly ILogger<SagaCompensator> _logger;

    public SagaCompensator(StepExecutor stepExecutor, ILogger<SagaCompensator> logger)
    {
        _stepExecutor = stepExecutor;
        _logger = logger;
    }

    /// <summary>
    /// Compensates all completed steps in reverse order.
    /// Returns true if all compensations succeeded; false if any failed.
    /// </summary>
    public async Task<bool> CompensateAsync<TData>(
        WorkflowInstance instance,
        WorkflowDefinition definition,
        CancellationToken ct)
        where TData : class, new()
    {
        _logger.LogWarning(
            "Starting saga compensation for workflow {WorkflowName} [{InstanceId}]",
            definition.Name, instance.Id);

        // Compensate in reverse order — skip the failed step itself, compensate predecessors.
        var completedPointers = instance.ExecutionPointers
            .Where(p => p.Status == StepStatus.Complete)
            .OrderByDescending(p => p.EndTime)
            .ToList();

        bool allSucceeded = true;

        foreach (var pointer in completedPointers)
        {
            var stepDef = definition.Steps.FirstOrDefault(s => s.Index == pointer.StepIndex);
            if (stepDef is null) continue;

            // Find compensation step: explicit CompensateWithStepIndex OR the step itself.
            StepDefinition? compDef = null;
            if (stepDef.CompensateWithStepIndex.HasValue)
            {
                compDef = definition.Steps.FirstOrDefault(s =>
                    s.Index == stepDef.CompensateWithStepIndex.Value);
            }
            else if (stepDef.IsCompensatable)
            {
                compDef = stepDef;
            }

            if (compDef is null) continue;

            pointer.Status = StepStatus.Compensating;

            try
            {
                await _stepExecutor.CompensateAsync<TData>(instance, compDef, pointer, ct);
                pointer.Status = StepStatus.Compensated;

                _logger.LogInformation(
                    "Compensated step {StepName} [{InstanceId}]",
                    stepDef.Name, instance.Id);
            }
            catch (Exception ex)
            {
                pointer.Status = StepStatus.Failed;
                allSucceeded = false;

                _logger.LogError(ex,
                    "Compensation of step {StepName} failed [{InstanceId}]",
                    stepDef.Name, instance.Id);
            }
        }

        return allSucceeded;
    }
}
