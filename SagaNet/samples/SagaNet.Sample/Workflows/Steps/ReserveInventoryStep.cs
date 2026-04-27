using Microsoft.Extensions.Logging;
using SagaNet.Core.Abstractions;
using SagaNet.Core.Models;
using SagaNet.Sample.Workflows;

namespace SagaNet.Sample.Workflows.Steps;

public sealed class ReserveInventoryStep : ICompensatableStep<OrderData>
{
    private readonly ILogger<ReserveInventoryStep> _logger;

    public ReserveInventoryStep(ILogger<ReserveInventoryStep> logger) => _logger = logger;

    public async Task<ExecutionResult> RunAsync(StepExecutionContext<OrderData> context)
    {
        _logger.LogInformation("Reserving inventory for order {OrderId}", context.Data.OrderId);

        // Simulate async inventory service call.
        await Task.Delay(50, context.CancellationToken);

        context.Data.InventoryReserved = true;
        return ExecutionResult.Next();
    }

    public async Task CompensateAsync(StepExecutionContext<OrderData> context)
    {
        _logger.LogWarning("Releasing inventory reservation for order {OrderId}", context.Data.OrderId);
        await Task.Delay(20, context.CancellationToken);
        context.Data.InventoryReserved = false;
    }
}
