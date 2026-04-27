using Microsoft.Extensions.Logging;
using Dragonfire.Saga.Core.Abstractions;
using Dragonfire.Saga.Core.Models;
using Dragonfire.Saga.Sample.Workflows;

namespace Dragonfire.Saga.Sample.Workflows.Steps;

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
