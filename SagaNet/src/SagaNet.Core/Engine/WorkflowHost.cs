using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SagaNet.Core.Abstractions;
using SagaNet.Core.Diagnostics;
using SagaNet.Core.Models;

namespace SagaNet.Core.Engine;

/// <summary>
/// <see cref="IHostedService"/> that continuously polls for runnable workflow instances
/// and dispatches them to <see cref="WorkflowExecutor"/> with configurable concurrency.
///
/// Uses <see cref="IServiceScopeFactory"/> so that each execution gets its own DI scope,
/// allowing scoped services such as EF Core DbContext to be used safely from this singleton.
/// </summary>
public sealed class WorkflowHost : BackgroundService, IWorkflowHost
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWorkflowRegistry _registry;
    private readonly IEventBroker _eventBroker;
    private readonly WorkflowOptions _options;
    private readonly ILogger<WorkflowHost> _logger;

    // Channel used to enqueue instance IDs discovered during polling.
    private readonly Channel<Guid> _workQueue;

    // Tracks which instance IDs are currently being executed so that
    // multiple workers never process the same instance simultaneously.
    // This is essential when using InMemory EF Core, which doesn't enforce
    // the RowVersion optimistic-concurrency check that SQL Server relies on.
    private readonly ConcurrentDictionary<Guid, byte> _inFlight = new();

    public WorkflowHost(
        IServiceScopeFactory scopeFactory,
        IWorkflowRegistry registry,
        IEventBroker eventBroker,
        IOptions<WorkflowOptions> options,
        ILogger<WorkflowHost> logger)
    {
        _scopeFactory = scopeFactory;
        _registry = registry;
        _eventBroker = eventBroker;
        _options = options.Value;
        _logger = logger;

        _workQueue = Channel.CreateBounded<Guid>(new BoundedChannelOptions(
            _options.PollBatchSize * 2)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = true
        });
    }

    // ── IWorkflowHost ────────────────────────────────────────────────────────

    public async Task<Guid> StartWorkflowAsync<TWorkflow, TData>(
        TData data,
        CancellationToken cancellationToken = default)
        where TWorkflow : IWorkflow<TData>
        where TData : class, new()
    {
        var definition = ResolveDefinition<TWorkflow, TData>();

        var instance = new WorkflowInstance
        {
            WorkflowName = definition.Name,
            Version = definition.Version,
            DataType = typeof(TData).AssemblyQualifiedName ?? typeof(TData).FullName!,
            DataJson = System.Text.Json.JsonSerializer.Serialize(data),
            Status = WorkflowStatus.Runnable,
            NextExecutionTime = DateTime.UtcNow
        };

        await using var scope = _scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IWorkflowRepository>();
        instance = await repository.CreateInstanceAsync(instance, cancellationToken);

        SagaNetDiagnostics.WorkflowsStarted.Add(1,
            new System.Diagnostics.TagList { { "workflow", definition.Name } });
        SagaNetDiagnostics.ActiveWorkflows.Add(1,
            new System.Diagnostics.TagList { { "workflow", definition.Name } });

        _logger.LogInformation(
            "Started workflow {WorkflowName} v{Version} [{InstanceId}]",
            definition.Name, definition.Version, instance.Id);

        // Eagerly enqueue for fast pick-up.
        _workQueue.Writer.TryWrite(instance.Id);

        return instance.Id;
    }

    public async Task PublishEventAsync(
        string eventName,
        string eventKey,
        object? eventData = null,
        CancellationToken cancellationToken = default)
    {
        var @event = new WorkflowEvent
        {
            EventName = eventName,
            EventKey = eventKey,
            EventDataJson = eventData is null
                ? null
                : System.Text.Json.JsonSerializer.Serialize(eventData)
        };

        await using var scope = _scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IWorkflowRepository>();
        await repository.PublishEventAsync(@event, cancellationToken);

        // Also notify in-process waiters immediately.
        await _eventBroker.PublishAsync(eventName, eventKey, eventData, cancellationToken);

        _logger.LogDebug("Published event {EventName}/{EventKey}", eventName, eventKey);
    }

    public async Task TerminateWorkflowAsync(
        Guid instanceId,
        CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IWorkflowRepository>();

        var instance = await repository.GetInstanceAsync(instanceId, cancellationToken);
        if (instance is null || instance.IsTerminal) return;

        instance.Status = WorkflowStatus.Terminated;
        instance.CompleteAt = DateTime.UtcNow;
        await repository.UpdateInstanceAsync(instance, cancellationToken);

        _logger.LogInformation("Terminated workflow [{InstanceId}]", instanceId);
    }

    // ── BackgroundService ────────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "WorkflowHost started (poll={PollInterval}, batch={Batch}, concurrency={Concurrency})",
            _options.PollInterval, _options.PollBatchSize, _options.MaxConcurrentWorkflows);

        // Start concurrent workers.
        var workers = Enumerable
            .Range(0, _options.MaxConcurrentWorkflows)
            .Select(_ => WorkerLoopAsync(stoppingToken))
            .ToArray();

        // Poll loop feeds the work queue.
        await PollLoopAsync(stoppingToken);

        _workQueue.Writer.Complete();
        await Task.WhenAll(workers);

        _logger.LogInformation("WorkflowHost stopped.");
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_options.PollInterval);
        while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var repository = scope.ServiceProvider.GetRequiredService<IWorkflowRepository>();
                var ids = await repository.GetRunnableInstanceIdsAsync(_options.PollBatchSize, ct);
                foreach (var id in ids)
                    _workQueue.Writer.TryWrite(id);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error polling for runnable workflow instances.");
            }
        }
    }

    private async Task WorkerLoopAsync(CancellationToken ct)
    {
        await foreach (var instanceId in _workQueue.Reader.ReadAllAsync(ct))
        {
            // Deduplicate: skip if another worker is already executing this instance.
            // Prevents double-execution when the same ID is queued multiple times before
            // it gets processed, or when InMemory EF Core doesn't enforce RowVersion.
            if (!_inFlight.TryAdd(instanceId, 0))
                continue;

            try
            {
                // Each workflow execution gets its own DI scope so that scoped services
                // (e.g. EF Core DbContext) are properly isolated and disposed afterwards.
                await using var scope = _scopeFactory.CreateAsyncScope();
                var executor = scope.ServiceProvider.GetRequiredService<WorkflowExecutor>();
                await executor.ExecuteAsync(instanceId, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Unhandled error processing workflow instance [{InstanceId}]", instanceId);
            }
            finally
            {
                _inFlight.TryRemove(instanceId, out _);
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private WorkflowDefinition ResolveDefinition<TWorkflow, TData>()
        where TWorkflow : IWorkflow<TData>
        where TData : class, new()
    {
        var wf = (IWorkflow<TData>)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(TWorkflow));

        var def = _registry.Find(wf.Name, wf.Version);
        if (def is null)
            throw new Exceptions.WorkflowException(
                $"Workflow '{wf.Name}' v{wf.Version} is not registered. " +
                $"Call builder.AddWorkflow<{typeof(TWorkflow).Name}>() during startup.");

        return def;
    }
}
