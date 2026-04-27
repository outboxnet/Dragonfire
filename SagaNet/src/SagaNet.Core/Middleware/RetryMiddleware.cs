using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SagaNet.Core.Abstractions;
using SagaNet.Core.Models;

namespace SagaNet.Core.Middleware;

/// <summary>
/// Converts unhandled exceptions thrown by a step into a <see cref="ExecutionResult.RetryResult"/>
/// (up to <see cref="RetryPolicy.MaxAttempts"/>) or a <see cref="ExecutionResult.FailResult"/>
/// when the limit is exceeded.
/// </summary>
public sealed class RetryMiddleware : IWorkflowMiddleware
{
    private readonly WorkflowOptions _options;
    private readonly ILogger<RetryMiddleware> _logger;

    public RetryMiddleware(IOptions<WorkflowOptions> options, ILogger<RetryMiddleware> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ExecutionResult> InvokeAsync(
        MiddlewareContext context,
        Func<Task<ExecutionResult>> next)
    {
        try
        {
            return await next();
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            // Host is shutting down; retry after restart.
            return ExecutionResult.Retry(
                _options.DefaultRetryPolicy.ComputeDelay(context.AttemptCount + 1),
                "Host shutdown during step execution.");
        }
        catch (Exception ex)
        {
            var policy = _options.DefaultRetryPolicy;
            var nextAttempt = context.AttemptCount + 1;

            if (nextAttempt >= policy.MaxAttempts)
            {
                _logger.LogError(ex,
                    "Step {StepName} exhausted all {MaxAttempts} retry attempts [{InstanceId}]",
                    context.StepName, policy.MaxAttempts, context.WorkflowInstanceId);

                return ExecutionResult.Fail(
                    $"Step '{context.StepName}' failed after {policy.MaxAttempts} attempts: {ex.Message}",
                    ex);
            }

            var delay = policy.ComputeDelay(nextAttempt);
            _logger.LogWarning(ex,
                "Step {StepName} threw an exception (attempt {Attempt}/{Max}); retrying in {Delay} [{InstanceId}]",
                context.StepName, nextAttempt, policy.MaxAttempts, delay, context.WorkflowInstanceId);

            return ExecutionResult.Retry(delay, ex.Message);
        }
    }
}
