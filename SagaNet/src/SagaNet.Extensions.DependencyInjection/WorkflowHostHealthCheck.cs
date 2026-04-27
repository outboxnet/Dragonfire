using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using SagaNet.Core.Engine;

namespace SagaNet.Extensions.DependencyInjection;

/// <summary>
/// Health check that reports Healthy when the <see cref="WorkflowHost"/> is running.
/// </summary>
public sealed class WorkflowHostHealthCheck : IHealthCheck
{
    private readonly WorkflowHost _host;

    public WorkflowHostHealthCheck(WorkflowHost host) => _host = host;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var result = _host.ExecuteTask?.IsCompleted == false
            ? HealthCheckResult.Healthy("WorkflowHost is running.")
            : HealthCheckResult.Degraded("WorkflowHost is not running or has stopped.");

        return Task.FromResult(result);
    }
}
