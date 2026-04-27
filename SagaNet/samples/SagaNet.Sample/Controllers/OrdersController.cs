using Microsoft.AspNetCore.Mvc;
using SagaNet.Core.Abstractions;
using SagaNet.Core.Models;
using SagaNet.Sample.Workflows;

namespace SagaNet.Sample.Controllers;

/// <summary>Request body for placing a new order.</summary>
/// <param name="OrderId">Your own order identifier, e.g. "ORD-001".</param>
/// <param name="Amount">Total charge amount in your currency.</param>
/// <param name="CustomerEmail">Customer email — used for the confirmation step.</param>
public sealed record PlaceOrderRequest(string OrderId, decimal Amount, string CustomerEmail);

/// <summary>Manages order workflow instances.</summary>
[ApiController]
[Route("orders")]
[Produces("application/json")]
public sealed class OrdersController : ControllerBase
{
    private readonly IWorkflowHost _workflows;
    private readonly IWorkflowQueryService _queries;

    public OrdersController(IWorkflowHost workflows, IWorkflowQueryService queries)
    {
        _workflows = workflows;
        _queries   = queries;
    }

    /// <summary>Start a new OrderWorkflow.</summary>
    /// <remarks>
    /// Immediately returns 202 Accepted with the workflow instance ID.
    /// The workflow runs in the background — poll the progress endpoint to track it.
    /// </remarks>
    /// <response code="202">Workflow accepted and started.</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> PlaceOrder(
        [FromBody] PlaceOrderRequest req,
        CancellationToken ct)
    {
        var instanceId = await _workflows.StartWorkflowAsync<OrderWorkflow, OrderData>(
            new OrderData
            {
                OrderId       = req.OrderId,
                Amount        = req.Amount,
                CustomerEmail = req.CustomerEmail
            }, ct);

        return AcceptedAtAction(
            nameof(GetProgress),
            new { instanceId },
            new { WorkflowInstanceId = instanceId });
    }

    /// <summary>Get the live progress snapshot for a workflow instance.</summary>
    /// <param name="instanceId">The GUID returned when the workflow was started.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Progress snapshot.</response>
    /// <response code="404">No workflow with that ID exists.</response>
    [HttpGet("{instanceId:guid}/progress")]
    [ProducesResponseType(typeof(WorkflowProgress), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProgress(Guid instanceId, CancellationToken ct)
    {
        var progress = await _queries.GetProgressAsync(instanceId, ct);
        return progress is null
            ? NotFound(new { error = "Workflow instance not found." })
            : Ok(progress);
    }

    /// <summary>List the most recent workflow instances.</summary>
    /// <param name="limit">Maximum number of results (default 20).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">List of workflow progress snapshots, newest first.</response>
    [HttpGet("recent")]
    [ProducesResponseType(typeof(IReadOnlyList<WorkflowProgress>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRecent(
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        var recent = await _queries.GetRecentAsync(limit, ct);
        return Ok(recent);
    }

    /// <summary>Terminate a running workflow.</summary>
    /// <param name="instanceId">The GUID of the workflow to cancel.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Workflow terminated.</response>
    [HttpPost("{instanceId:guid}/terminate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Terminate(Guid instanceId, CancellationToken ct)
    {
        await _workflows.TerminateWorkflowAsync(instanceId, ct);
        return Ok(new { message = "Workflow terminated." });
    }
}
