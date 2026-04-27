using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SagaNet.Core.Engine;
using SagaNet.Core.Models;
using SagaNet.Core.Registry;
using SagaNet.Tests.Helpers;
using Xunit;

namespace SagaNet.Tests.Engine;

public sealed class SagaCompensatorTests
{
    /// <summary>
    /// After the builder wires OrderWorkflow, the forward-flow indices are:
    /// 0=ValidateOrder, 1=ReserveInventory, 2=Compensate:RI, 3=ProcessPayment, 4=Compensate:PP, 5=ShipOrder.
    /// This test marks steps 1 (RI) and 3 (PP) as Complete and expects both to be compensated.
    /// </summary>
    [Fact]
    public async Task CompensateAsync_CompensatesAllCompletedStepsInReverseOrder()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions<WorkflowOptions>();
        services.AddTransient<ReserveInventoryStep>();
        services.AddTransient<ProcessPaymentStep>();
        services.AddTransient<ValidateOrderStep>();
        services.AddTransient<ShipOrderStep>();

        var sp = services.BuildServiceProvider();
        var options = Options.Create(new WorkflowOptions());

        var stepExecutor = new StepExecutor(
            sp,
            Enumerable.Empty<Core.Abstractions.IWorkflowMiddleware>(),
            options,
            NullLogger<StepExecutor>.Instance);

        var compensator = new SagaCompensator(stepExecutor, NullLogger<SagaCompensator>.Instance);

        // Start with both flags set — compensation should clear them.
        var data = new OrderData { InventoryReserved = true, PaymentProcessed = true };
        var dataJson = System.Text.Json.JsonSerializer.Serialize(data);

        var instance = new WorkflowInstance
        {
            WorkflowName = "OrderWorkflow",
            Version = 1,
            DataType = typeof(OrderData).AssemblyQualifiedName!,
            DataJson = dataJson,
            ExecutionPointers =
            [
                // Step 1 = ReserveInventory (CompensateWithStepIndex=2)
                new ExecutionPointer
                {
                    StepIndex = 1,
                    StepName = "ReserveInventory",
                    Status = StepStatus.Complete,
                    EndTime = DateTime.UtcNow.AddSeconds(-2)
                },
                // Step 3 = ProcessPayment (CompensateWithStepIndex=4)
                new ExecutionPointer
                {
                    StepIndex = 3,
                    StepName = "ProcessPayment",
                    Status = StepStatus.Complete,
                    EndTime = DateTime.UtcNow.AddSeconds(-1)
                }
            ]
        };

        var registry = new WorkflowRegistry(sp);
        registry.Register<OrderWorkflow, OrderData>();
        var definition = registry.Find("OrderWorkflow", 1)!;

        bool succeeded = await compensator.CompensateAsync<OrderData>(
            instance, definition, CancellationToken.None);

        succeeded.Should().BeTrue();
        var result = System.Text.Json.JsonSerializer.Deserialize<OrderData>(instance.DataJson)!;
        result.PaymentProcessed.Should().BeFalse("payment should be refunded");
        result.InventoryReserved.Should().BeFalse("inventory reservation should be released");
    }
}
