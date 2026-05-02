using Dragonfire.TraceKit.SampleApp.Storage;
using Microsoft.AspNetCore.Mvc;

namespace Dragonfire.TraceKit.SampleApp.Controllers;

[Route("traces")]
public sealed class TracesController : Controller
{
    private readonly ITraceQuery _query;

    public TracesController(ITraceQuery query) => _query = query;

    [HttpGet("")]
    public IActionResult Index()
    {
        var sessions = _query.ListSessions(take: 200);
        return View(sessions);
    }

    [HttpGet("{correlationId}")]
    public IActionResult Details(string correlationId)
    {
        var rows = _query.GetSession(correlationId);
        if (rows.Count == 0) return NotFound();
        ViewData["CorrelationId"] = correlationId;
        return View(rows);
    }
}
