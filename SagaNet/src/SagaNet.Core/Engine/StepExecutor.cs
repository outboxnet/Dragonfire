using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SagaNet.Core.Abstractions;
using SagaNet.Core.Middleware;
using SagaNet.Core.Models;

namespace SagaNet.Core.Engine;

/// <summary>
/// Resolves the step type from DI, builds the execution context, and runs the step
/// through the <see cref="MiddlewarePipeline"/>.
/// </summary>
public sealed class StepExecutor
{
    private readonly IServiceProvider _services;
    private readonly MiddlewarePipeline _pipeline;
    private readonly WorkflowOptions _options;
    private readonly ILogger<StepExecutor> _logger;

    public StepExecutor(
        IServiceProvider services,
        IEnumerable<IWorkflowMiddleware> middlewares,
        IOptions<WorkflowOptions> options,
        ILogger<StepExecutor> logger)
    {
        _services = services;
        _pipeline = new MiddlewarePipeline(middlewares);
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Executes a step and returns the raw <see cref="ExecutionResult"/>.</summary>
    public async Task<ExecutionResult> ExecuteAsync<TData>(
        WorkflowInstance instance,
        WorkflowDefinition definition,
        StepDefinition stepDef,
        ExecutionPointer pointer,
        CancellationToken ct)
        where TData : class, new()
    {
        var data = JsonSerializer.Deserialize<TData>(instance.DataJson)
                   ?? new TData();

        using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stepCts.CancelAfter(_options.StepTimeout);

        var stepContext = new StepExecutionContext<TData>
        {
            WorkflowInstanceId = instance.Id,
            Data = data,
            Pointer = pointer,
            StepDefinition = stepDef,
            CancellationToken = stepCts.Token
        };

        var middlewareContext = new MiddlewareContext
        {
            WorkflowInstanceId = instance.Id,
            WorkflowName = definition.Name,
            StepName = stepDef.Name,
            AttemptCount = pointer.AttemptCount,
            CancellationToken = stepCts.Token
        };

        ExecutionResult result = await _pipeline.ExecuteAsync(
            middlewareContext,
            async () =>
            {
                var step = (IWorkflowStep<TData>)ActivatorUtilities.CreateInstance(
                    _services, stepDef.StepType);

                return await step.RunAsync(stepContext);
            });

        // Persist any mutations the step made to data back to the instance.
        instance.DataJson = JsonSerializer.Serialize(data);

        return result;
    }

    /// <summary>Executes the compensation action of a compensatable step.</summary>
    public async Task CompensateAsync<TData>(
        WorkflowInstance instance,
        StepDefinition stepDef,
        ExecutionPointer pointer,
        CancellationToken ct)
        where TData : class, new()
    {
        var data = JsonSerializer.Deserialize<TData>(instance.DataJson) ?? new TData();

        using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stepCts.CancelAfter(_options.StepTimeout);

        var stepContext = new StepExecutionContext<TData>
        {
            WorkflowInstanceId = instance.Id,
            Data = data,
            Pointer = pointer,
            StepDefinition = stepDef,
            CancellationToken = stepCts.Token
        };

        var step = (ICompensatableStep<TData>)ActivatorUtilities.CreateInstance(
            _services, stepDef.StepType);

        try
        {
            await step.CompensateAsync(stepContext);
            instance.DataJson = JsonSerializer.Serialize(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Compensation failed for step {StepName} [{InstanceId}]",
                stepDef.Name, instance.Id);
            throw;
        }
    }
}
