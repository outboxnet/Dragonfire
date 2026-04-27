using Microsoft.Extensions.DependencyInjection;
using SagaNet.Core.Abstractions;
using SagaNet.Core.Models;

namespace SagaNet.Extensions.DependencyInjection;

/// <summary>
/// Fluent builder returned by <see cref="ServiceCollectionExtensions.AddSagaNet"/>.
/// Use it to register workflows, custom middleware, and configure options.
/// </summary>
public sealed class SagaNetBuilder
{
    internal SagaNetBuilder(IServiceCollection services) => Services = services;

    public IServiceCollection Services { get; }

    /// <summary>
    /// Registers a workflow definition. The workflow and its steps are resolved
    /// from the DI container, so you can inject dependencies into steps normally.
    /// </summary>
    public SagaNetBuilder AddWorkflow<TWorkflow, TData>()
        where TWorkflow : class, IWorkflow<TData>
        where TData : class, new()
    {
        // Register workflow as transient so the registry can instantiate it.
        Services.AddTransient<TWorkflow>();
        Services.AddSingleton<WorkflowRegistration>(sp =>
            new WorkflowRegistration(typeof(TWorkflow), typeof(TData)));
        return this;
    }

    /// <summary>Adds a custom middleware to the step execution pipeline.</summary>
    public SagaNetBuilder AddMiddleware<TMiddleware>()
        where TMiddleware : class, IWorkflowMiddleware
    {
        Services.AddTransient<IWorkflowMiddleware, TMiddleware>();
        return this;
    }

    /// <summary>Configures the workflow engine options inline.</summary>
    public SagaNetBuilder Configure(Action<WorkflowOptions> configure)
    {
        Services.Configure(configure);
        return this;
    }
}

/// <summary>Marker used to delay workflow registration until after the DI container is built.</summary>
internal sealed record WorkflowRegistration(Type WorkflowType, Type DataType);
