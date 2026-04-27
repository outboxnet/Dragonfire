using SagaNet.Core.Abstractions;
using SagaNet.Core.Models;

namespace SagaNet.Core.Middleware;

/// <summary>
/// Builds and executes a chain of <see cref="IWorkflowMiddleware"/> around a step delegate.
/// </summary>
internal sealed class MiddlewarePipeline
{
    private readonly IReadOnlyList<IWorkflowMiddleware> _middlewares;

    public MiddlewarePipeline(IEnumerable<IWorkflowMiddleware> middlewares)
        => _middlewares = [.. middlewares];

    public Task<ExecutionResult> ExecuteAsync(
        MiddlewareContext context,
        Func<Task<ExecutionResult>> core)
    {
        if (_middlewares.Count == 0)
            return core();

        return BuildChain(0, context, core)();
    }

    private Func<Task<ExecutionResult>> BuildChain(
        int index,
        MiddlewareContext context,
        Func<Task<ExecutionResult>> core)
    {
        if (index == _middlewares.Count)
            return core;

        var current = _middlewares[index];
        var next = BuildChain(index + 1, context, core);
        return () => current.InvokeAsync(context, next);
    }
}
