using SagaNet.Core.Models;

namespace SagaNet.Core.Abstractions;

/// <summary>
/// Middleware that wraps step execution. Chain multiple middleware to add cross-cutting concerns
/// such as retry, logging, metrics, and distributed tracing.
/// </summary>
public interface IWorkflowMiddleware
{
    /// <summary>
    /// Called to execute the next element in the middleware pipeline.
    /// </summary>
    Task<ExecutionResult> InvokeAsync(
        MiddlewareContext context,
        Func<Task<ExecutionResult>> next);
}
