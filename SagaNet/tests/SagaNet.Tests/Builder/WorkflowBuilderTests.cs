using FluentAssertions;
using SagaNet.Core.Builder;
using SagaNet.Tests.Helpers;
using Xunit;

namespace SagaNet.Tests.Builder;

public sealed class WorkflowBuilderTests
{
    [Fact]
    public void Build_LinearWorkflow_ProducesCorrectStepCount()
    {
        var builder = new WorkflowBuilder<OrderData>();
        builder
            .StartWith<ValidateOrderStep>()
            .Then<ReserveInventoryStep>()
            .Then<ProcessPaymentStep>()
            .Then<ShipOrderStep>();

        var definition = builder.Build("Test", 1, typeof(OrderData));

        definition.Steps.Should().HaveCount(4);
    }

    [Fact]
    public void Build_StepsHaveCorrectIndices()
    {
        var builder = new WorkflowBuilder<OrderData>();
        builder
            .StartWith<ValidateOrderStep>()
            .Then<ReserveInventoryStep>()
            .Then<ShipOrderStep>();

        var definition = builder.Build("Test", 1, typeof(OrderData));

        definition.Steps[0].Index.Should().Be(0);
        definition.Steps[1].Index.Should().Be(1);
        definition.Steps[2].Index.Should().Be(2);
    }

    [Fact]
    public void Build_LinearSequence_WiresNextStepIndices()
    {
        var builder = new WorkflowBuilder<OrderData>();
        builder
            .StartWith<ValidateOrderStep>()
            .Then<ReserveInventoryStep>()
            .Then<ShipOrderStep>();

        var definition = builder.Build("Test", 1, typeof(OrderData));

        definition.Steps[0].NextStepIndices.Should().Contain(1);
        definition.Steps[1].NextStepIndices.Should().Contain(2);
        definition.Steps[2].NextStepIndices.Should().BeEmpty();
    }

    [Fact]
    public void Build_CompensatableStep_MarkedCorrectly()
    {
        var builder = new WorkflowBuilder<OrderData>();
        builder
            .StartWith<ReserveInventoryStep>()
                .CompensateWith<ReserveInventoryStep>()
            .Then<ShipOrderStep>();

        var definition = builder.Build("Test", 1, typeof(OrderData));

        definition.Steps[0].IsCompensatable.Should().BeTrue();
    }

    [Fact]
    public void Build_WithRetryPolicy_PolicyIsAttached()
    {
        var builder = new WorkflowBuilder<OrderData>();
        builder
            .StartWith<ValidateOrderStep>()
                .WithRetry(r => r.MaxAttempts(5).InitialDelay(TimeSpan.FromSeconds(2)));

        var definition = builder.Build("Test", 1, typeof(OrderData));

        definition.Steps[0].RetryPolicy!.MaxAttempts.Should().Be(5);
        definition.Steps[0].RetryPolicy!.InitialDelay.Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Build_WithDescription_DescriptionIsSet()
    {
        var builder = new WorkflowBuilder<OrderData>();
        builder.StartWith<ValidateOrderStep>().WithDescription("Validates the order data");

        var definition = builder.Build("Test", 1, typeof(OrderData));

        definition.Steps[0].Description.Should().Be("Validates the order data");
    }

    [Fact]
    public void Build_WorkflowNameAndVersionSet()
    {
        var builder = new WorkflowBuilder<OrderData>();
        builder.StartWith<ValidateOrderStep>();

        var definition = builder.Build("MyWorkflow", 3, typeof(OrderData));

        definition.Name.Should().Be("MyWorkflow");
        definition.Version.Should().Be(3);
        definition.DataType.Should().Be(typeof(OrderData));
    }
}
