using SagaNet.Core.Abstractions;
using SagaNet.Core.Builder;
using SagaNet.Core.Models;

namespace SagaNet.Tests.Helpers;

// ── Data ────────────────────────────────────────────────────────────────────

public class OrderData
{
    public string OrderId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool InventoryReserved { get; set; }
    public bool PaymentProcessed { get; set; }
    public bool Shipped { get; set; }

    /// <summary>Set to true in test data to trigger payment failure.</summary>
    public bool SimulatePaymentFailure { get; set; }
}

// ── Steps ────────────────────────────────────────────────────────────────────

public class ValidateOrderStep : IWorkflowStep<OrderData>
{
    public Task<ExecutionResult> RunAsync(StepExecutionContext<OrderData> context)
    {
        if (string.IsNullOrEmpty(context.Data.OrderId))
            return Task.FromResult(ExecutionResult.Fail("OrderId is required."));

        return Task.FromResult(ExecutionResult.Next());
    }
}

public class ReserveInventoryStep : ICompensatableStep<OrderData>
{
    public Task<ExecutionResult> RunAsync(StepExecutionContext<OrderData> context)
    {
        context.Data.InventoryReserved = true;
        return Task.FromResult(ExecutionResult.Next());
    }

    public Task CompensateAsync(StepExecutionContext<OrderData> context)
    {
        context.Data.InventoryReserved = false;
        return Task.CompletedTask;
    }
}

public class ProcessPaymentStep : ICompensatableStep<OrderData>
{
    public Task<ExecutionResult> RunAsync(StepExecutionContext<OrderData> context)
    {
        if (context.Data.SimulatePaymentFailure)
            return Task.FromResult(ExecutionResult.Fail("Payment gateway error."));

        context.Data.PaymentProcessed = true;
        return Task.FromResult(ExecutionResult.Next());
    }

    public Task CompensateAsync(StepExecutionContext<OrderData> context)
    {
        context.Data.PaymentProcessed = false;
        return Task.CompletedTask;
    }
}

public class ShipOrderStep : IWorkflowStep<OrderData>
{
    public Task<ExecutionResult> RunAsync(StepExecutionContext<OrderData> context)
    {
        context.Data.Shipped = true;
        return Task.FromResult(ExecutionResult.Complete());
    }
}

public class ThrowingStep : IWorkflowStep<OrderData>
{
    public Task<ExecutionResult> RunAsync(StepExecutionContext<OrderData> context)
        => throw new InvalidOperationException("Simulated transient error.");
}

// ── Workflows ────────────────────────────────────────────────────────────────

/// <summary>
/// Forward flow: ValidateOrder(0) → ReserveInventory(1) → [Compensate:RI(2)] → ProcessPayment(3) → [Compensate:PP(4)] → ShipOrder(5)
/// </summary>
public class OrderWorkflow : IWorkflow<OrderData>
{
    public string Name => "OrderWorkflow";

    public void Build(IWorkflowBuilder<OrderData> builder)
    {
        builder
            .StartWith<ValidateOrderStep>("ValidateOrder")
            .Then<ReserveInventoryStep>("ReserveInventory")
                .CompensateWith<ReserveInventoryStep>()
            .Then<ProcessPaymentStep>("ProcessPayment")
                .CompensateWith<ProcessPaymentStep>()
            .Then<ShipOrderStep>("ShipOrder");
    }
}

public class ThrowingWorkflow : IWorkflow<OrderData>
{
    public string Name => "ThrowingWorkflow";

    public void Build(IWorkflowBuilder<OrderData> builder)
    {
        builder
            .StartWith<ThrowingStep>("ThrowingStep")
                .WithRetry(r => r.MaxAttempts(2).InitialDelay(TimeSpan.Zero));
    }
}
