using Microsoft.Extensions.Logging;
using Dragonfire.Saga.Core.Abstractions;
using Dragonfire.Saga.Core.Models;
using Dragonfire.Saga.Sample.Workflows;

namespace Dragonfire.Saga.Sample.Workflows.Steps;

public sealed class ValidateOrderStep : IWorkflowStep<OrderData>
{
    private readonly ILogger<ValidateOrderStep> _logger;

    public ValidateOrderStep(ILogger<ValidateOrderStep> logger) => _logger = logger;

    public Task<ExecutionResult> RunAsync(StepExecutionContext<OrderData> context)
    {
        _logger.LogInformation("Validating order {OrderId}", context.Data.OrderId);

        if (string.IsNullOrWhiteSpace(context.Data.OrderId))
            return Task.FromResult(ExecutionResult.Fail("OrderId cannot be empty."));

        if (context.Data.Amount <= 0)
            return Task.FromResult(ExecutionResult.Fail("Order amount must be positive."));

        return Task.FromResult(ExecutionResult.Next());
    }
}
