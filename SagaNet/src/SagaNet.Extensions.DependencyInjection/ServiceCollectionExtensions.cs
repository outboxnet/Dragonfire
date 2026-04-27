using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SagaNet.Core.Abstractions;
using SagaNet.Core.Engine;
using SagaNet.Core.Events;
using SagaNet.Core.Middleware;
using SagaNet.Core.Models;
using SagaNet.Core.Registry;

namespace SagaNet.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering SagaNet into an <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the SagaNet workflow engine to the DI container.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddSagaNet(builder =>
    /// {
    ///     builder
    ///         .AddWorkflow&lt;OrderWorkflow, OrderData&gt;()
    ///         .Configure(opt => opt.PollInterval = TimeSpan.FromSeconds(1));
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddSagaNet(
        this IServiceCollection services,
        Action<SagaNetBuilder>? configure = null)
    {
        // Options
        services.AddOptions<WorkflowOptions>()
            .BindConfiguration(WorkflowOptions.SectionName);

        // Singletons — safe to share across requests and the background host.
        services.TryAddSingleton<IWorkflowRegistry>(sp =>
        {
            var registry = new WorkflowRegistry(sp);
            foreach (var reg in sp.GetServices<WorkflowRegistration>())
            {
                typeof(IWorkflowRegistry)
                    .GetMethod(nameof(IWorkflowRegistry.Register))!
                    .MakeGenericMethod(reg.WorkflowType, reg.DataType)
                    .Invoke(registry, null);
            }
            return registry;
        });

        services.TryAddSingleton<IEventBroker, InMemoryEventBroker>();

        // Engine internals — registered as Scoped so each per-execution DI scope
        // (created by WorkflowHost via IServiceScopeFactory) gets a fresh set that
        // shares the same scoped IWorkflowRepository / DbContext.
        services.TryAddScoped<StepExecutor>();
        services.TryAddScoped<SagaCompensator>();
        services.TryAddScoped<WorkflowExecutor>();

        // Built-in middleware (order: ObservabilityMiddleware wraps RetryMiddleware).
        services.AddScoped<IWorkflowMiddleware, ObservabilityMiddleware>();
        services.AddScoped<IWorkflowMiddleware, RetryMiddleware>();

        // WorkflowHost is a Singleton IHostedService. It uses IServiceScopeFactory
        // internally so it never directly holds any scoped dependency.
        services.TryAddSingleton<WorkflowHost>();
        services.TryAddSingleton<IWorkflowHost>(sp => sp.GetRequiredService<WorkflowHost>());
        services.AddHostedService(sp => sp.GetRequiredService<WorkflowHost>());

        // Apply builder configuration.
        var builder = new SagaNetBuilder(services);
        configure?.Invoke(builder);

        return services;
    }

    /// <summary>
    /// Registers <see cref="IWorkflowQueryService"/> using the provided EF Core implementation type.
    /// Call this after <see cref="AddSagaNet"/> when using the EF Core persistence package.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddSagaNet(...);
    /// services.AddSagaNetQueries&lt;EfWorkflowQueryService&gt;();
    /// </code>
    /// </example>
    public static IServiceCollection AddSagaNetQueries<TQueryService>(
        this IServiceCollection services)
        where TQueryService : class, Core.Abstractions.IWorkflowQueryService
    {
        services.TryAddScoped<Core.Abstractions.IWorkflowQueryService, TQueryService>();
        return services;
    }

    /// <summary>
    /// Adds a SagaNet health check that reports Healthy when the host is running.
    /// </summary>
    public static IHealthChecksBuilder AddSagaNetHealthCheck(
        this IHealthChecksBuilder builder,
        string name = "saganet-workflow-host")
    {
        builder.AddCheck<WorkflowHostHealthCheck>(name);
        return builder;
    }
}
