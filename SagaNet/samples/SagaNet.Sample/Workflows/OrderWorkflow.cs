using SagaNet.Core.Abstractions;
using SagaNet.Core.Builder;
using SagaNet.Sample.Workflows.Steps;

namespace SagaNet.Sample.Workflows;

/// <summary>
/// Demonstrates a full e-commerce order saga with compensation.
///
/// Happy path:  ValidateOrder → ReserveInventory → ProcessPayment → ShipOrder
/// On failure:  Compensation runs in reverse: RefundPayment → ReleaseInventory
/// </summary>
public sealed class OrderWorkflow : IWorkflow<OrderData>
{
    public string Name => "OrderWorkflow";
    public int Version => 1;

    public void Build(IWorkflowBuilder<OrderData> builder)
    {
        builder
            .StartWith<ValidateOrderStep>("ValidateOrder")
                .WithDescription("Validates that the order has all required fields.")

            .Then<ReserveInventoryStep>("ReserveInventory")
                .CompensateWith<ReserveInventoryStep>()
                .WithDescription("Reserves items in the warehouse.")
                .WithRetry(r => r.MaxAttempts(3).InitialDelay(TimeSpan.FromSeconds(2)))

            .Then<ProcessPaymentStep>("ProcessPayment")
                .CompensateWith<ProcessPaymentStep>()
                .WithDescription("Charges the customer's payment method.")
                .WithRetry(r => r.MaxAttempts(2).InitialDelay(TimeSpan.FromSeconds(5)))

            .Then<ShipOrderStep>("ShipOrder")
                .WithDescription("Dispatches the parcel with the courier.");
    }
}
