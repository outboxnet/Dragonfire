using Microsoft.Extensions.Logging;
using SagaNet.Core.Abstractions;
using SagaNet.Core.Models;
using SagaNet.Sample.Workflows;

namespace SagaNet.Sample.Workflows.Steps;

public sealed class ProcessPaymentStep : ICompensatableStep<OrderData>
{
    private readonly ILogger<ProcessPaymentStep> _logger;

    public ProcessPaymentStep(ILogger<ProcessPaymentStep> logger) => _logger = logger;

    public async Task<ExecutionResult> RunAsync(StepExecutionContext<OrderData> context)
    {
        _logger.LogInformation(
            "Processing payment of {Amount:C} for order {OrderId}",
            context.Data.Amount, context.Data.OrderId);

        await Task.Delay(100, context.CancellationToken);

        context.Data.PaymentTransactionId = $"TXN-{Guid.NewGuid():N}";
        context.Data.PaymentProcessed = true;
        return ExecutionResult.Next();
    }

    public async Task CompensateAsync(StepExecutionContext<OrderData> context)
    {
        if (context.Data.PaymentTransactionId is null) return;

        _logger.LogWarning(
            "Refunding payment {TxnId} for order {OrderId}",
            context.Data.PaymentTransactionId, context.Data.OrderId);

        await Task.Delay(50, context.CancellationToken);
        context.Data.PaymentProcessed = false;
    }
}
