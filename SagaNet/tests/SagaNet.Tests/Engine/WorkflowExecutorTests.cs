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

public sealed class WorkflowExecutorTests
{
    private readonly IServiceProvider _services;
    private readonly InMemoryWorkflowRepository _repository;
    private readonly WorkflowExecutor _executor;

    public WorkflowExecutorTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions<WorkflowOptions>();

        services.AddTransient<ValidateOrderStep>();
        services.AddTransient<ReserveInventoryStep>();
        services.AddTransient<ProcessPaymentStep>();
        services.AddTransient<ShipOrderStep>();
        services.AddTransient<ThrowingStep>();

        _services = services.BuildServiceProvider();
        _repository = new InMemoryWorkflowRepository();

        var registry = new WorkflowRegistry(_services);
        registry.Register<OrderWorkflow, OrderData>();
        registry.Register<ThrowingWorkflow, OrderData>();

        var options = Options.Create(new WorkflowOptions
        {
            DefaultRetryPolicy = new RetryPolicy
            {
                MaxAttempts = 2,
                InitialDelay = TimeSpan.Zero,
                BackoffMultiplier = 1.0
            }
        });

        var middlewares = new Core.Abstractions.IWorkflowMiddleware[]
        {
            new Core.Middleware.RetryMiddleware(
                options, NullLogger<Core.Middleware.RetryMiddleware>.Instance)
        };

        var stepExecutor = new StepExecutor(
            _services, middlewares, options,
            NullLogger<StepExecutor>.Instance);

        var compensator = new SagaCompensator(
            stepExecutor, NullLogger<SagaCompensator>.Instance);

        _executor = new WorkflowExecutor(
            registry, _repository, stepExecutor, compensator,
            NullLogger<WorkflowExecutor>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_HappyPath_WorkflowCompletes()
    {
        var instance = await CreateOrderInstance(
            new OrderData { OrderId = "ORD-001", Amount = 99.99m });

        await RunToTerminalAsync(instance.Id);

        var final = (await _repository.GetInstanceAsync(instance.Id))!;
        final.Status.Should().Be(WorkflowStatus.Complete);

        var data = Deserialize(final.DataJson);
        data.InventoryReserved.Should().BeTrue();
        data.PaymentProcessed.Should().BeTrue();
        data.Shipped.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_StepFails_SagaCompensationTriggered()
    {
        // SimulatePaymentFailure instructs ProcessPaymentStep to return Fail.
        var instance = await CreateOrderInstance(
            new OrderData { OrderId = "ORD-002", Amount = 50m, SimulatePaymentFailure = true });

        await RunToTerminalAsync(instance.Id);

        var final = (await _repository.GetInstanceAsync(instance.Id))!;
        final.Status.Should().Be(WorkflowStatus.Compensated);

        var data = Deserialize(final.DataJson);
        data.InventoryReserved.Should().BeFalse("reservation should be rolled back");
        data.PaymentProcessed.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_ValidationFails_WorkflowFails()
    {
        var instance = await CreateOrderInstance(
            new OrderData { OrderId = "", Amount = 10m });

        await RunToTerminalAsync(instance.Id);

        var final = (await _repository.GetInstanceAsync(instance.Id))!;
        final.Status.Should().BeOneOf(WorkflowStatus.Failed, WorkflowStatus.Compensated);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownWorkflow_MarksInstanceFailed()
    {
        var instance = new WorkflowInstance
        {
            WorkflowName = "NonExistentWorkflow",
            Version = 1,
            DataType = typeof(OrderData).FullName!,
            DataJson = "{}",
            Status = WorkflowStatus.Runnable
        };
        await _repository.CreateInstanceAsync(instance);

        await _executor.ExecuteAsync(instance.Id, CancellationToken.None);

        var final = (await _repository.GetInstanceAsync(instance.Id))!;
        final.Status.Should().Be(WorkflowStatus.Failed);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<WorkflowInstance> CreateOrderInstance(OrderData data)
    {
        var instance = new WorkflowInstance
        {
            WorkflowName = "OrderWorkflow",
            Version = 1,
            DataType = typeof(OrderData).AssemblyQualifiedName!,
            DataJson = System.Text.Json.JsonSerializer.Serialize(data),
            Status = WorkflowStatus.Runnable
        };
        return await _repository.CreateInstanceAsync(instance);
    }

    private async Task RunToTerminalAsync(Guid instanceId, int maxTicks = 15)
    {
        for (int i = 0; i < maxTicks; i++)
        {
            await _executor.ExecuteAsync(instanceId, CancellationToken.None);
            var instance = (await _repository.GetInstanceAsync(instanceId))!;
            if (instance.IsTerminal) return;
            // Re-mark Runnable so TryAcquire picks it up again.
            instance.Status = WorkflowStatus.Runnable;
        }
    }

    private static OrderData Deserialize(string json)
        => System.Text.Json.JsonSerializer.Deserialize<OrderData>(json)!;
}
