using Microsoft.Extensions.Logging;
using SagaNet.Core.Abstractions;
using SagaNet.Core.Models;
using SagaNet.Sample.Workflows;

namespace SagaNet.Sample.Workflows.Steps;

public sealed class ShipOrderStep : IWorkflowStep<OrderData>
{
    private readonly ILogger<ShipOrderStep> _logger;

    public ShipOrderStep(ILogger<ShipOrderStep> logger) => _logger = logger;

    public async Task<ExecutionResult> RunAsync(StepExecutionContext<OrderData> context)
    {
        _logger.LogInformation("Shipping order {OrderId} to {Email}",
            context.Data.OrderId, context.Data.CustomerEmail);

        await Task.Delay(75, context.CancellationToken);

        context.Data.Shipped = true;
        return ExecutionResult.Complete();
    }
}
