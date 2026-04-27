using Microsoft.AspNetCore.Mvc;
using SagaNet.Core.Abstractions;
using SagaNet.Core.Models;

namespace SagaNet.Sample.Controllers;

/// <summary>Admin queries for inspecting workflow state by step.</summary>
[ApiController]
[Route("admin/steps")]
[Produces("application/json")]
public sealed class AdminController : ControllerBase
{
    private readonly IWorkflowQueryService _queries;

    public AdminController(IWorkflowQueryService queries) => _queries = queries;

    /// <summary>
    /// List all workflow instances where the named step is currently active
    /// (status = Pending, Running, or WaitingForEvent).
    /// </summary>
    /// <param name="stepName">
    /// Step name as declared in the workflow builder, e.g. <c>ProcessPayment</c>.
    /// </param>
    /// <param name="workflowName">Filter to a specific workflow type, e.g. <c>OrderWorkflow</c>.</param>
    /// <param name="createdAfterHours">Only include instances created within the last N hours.</param>
    /// <param name="limit">Maximum results to return (default 50).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Matching workflow instances.</response>
    [HttpGet("{stepName}/active")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActive(
        string stepName,
        [FromQuery] string? workflowName       = null,
        [FromQuery] double? createdAfterHours  = null,
        [FromQuery] int     limit              = 50,
        CancellationToken   ct                 = default)
    {
        var filter = new StepQueryFilter
        {
            WorkflowName = workflowName,
            CreatedAfter = createdAfterHours.HasValue
                ? DateTime.UtcNow.AddHours(-createdAfterHours.Value)
                : null,
            Limit = limit
        };

        var results = await _queries.GetStuckAtStepAsync(stepName, filter, ct);
        return Ok(new { stepName, count = results.Count, items = results });
    }

    /// <summary>
    /// List all workflow instances where the named step has permanently failed
    /// (all retries exhausted).
    /// </summary>
    /// <param name="stepName">
    /// Step name as declared in the workflow builder, e.g. <c>ProcessPayment</c>.
    /// </param>
    /// <param name="workflowName">Filter to a specific workflow type, e.g. <c>OrderWorkflow</c>.</param>
    /// <param name="createdAfterHours">Only include instances created within the last N hours.</param>
    /// <param name="limit">Maximum results to return (default 50).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Matching workflow instances.</response>
    [HttpGet("{stepName}/failed")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFailed(
        string stepName,
        [FromQuery] string? workflowName       = null,
        [FromQuery] double? createdAfterHours  = null,
        [FromQuery] int     limit              = 50,
        CancellationToken   ct                 = default)
    {
        var filter = new StepQueryFilter
        {
            WorkflowName = workflowName,
            CreatedAfter = createdAfterHours.HasValue
                ? DateTime.UtcNow.AddHours(-createdAfterHours.Value)
                : null,
            Limit = limit
        };

        var results = await _queries.GetFailedAtStepAsync(stepName, filter, ct);
        return Ok(new { stepName, count = results.Count, items = results });
    }
}
