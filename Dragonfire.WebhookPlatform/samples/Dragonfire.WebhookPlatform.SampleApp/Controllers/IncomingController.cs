using Dragonfire.Inbox.EntityFrameworkCore;
using Dragonfire.Inbox.Models;
using Dragonfire.WebhookPlatform.SampleApp.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dragonfire.WebhookPlatform.SampleApp.Controllers;

public sealed class IncomingController : Controller
{
    private const int PageSize = 100;
    private readonly InboxDbContext _db;

    public IncomingController(InboxDbContext db) => _db = db;

    // GET /Incoming[?eventType=...&jsonPath=$.x&jsonValue=...]
    public async Task<IActionResult> Index([FromQuery] EventFilter filter, CancellationToken ct)
    {
        IQueryable<InboxMessage> query = BuildFilteredQuery(filter);

        var messages = await query
            .AsNoTracking()
            .OrderByDescending(m => m.ReceivedAt)
            .Take(PageSize)
            .ToListAsync(ct);

        var ids = messages.Select(m => m.Id).ToList();
        var attempts = await _db.InboxHandlerAttempts
            .AsNoTracking()
            .Where(a => ids.Contains(a.InboxMessageId))
            .OrderByDescending(a => a.AttemptedAt)
            .ToListAsync(ct);

        var knownEventTypes = await _db.InboxMessages
            .AsNoTracking()
            .Select(m => m.EventType)
            .Distinct()
            .OrderBy(e => e)
            .ToListAsync(ct);

        var vm = new IncomingListViewModel
        {
            Filter = filter,
            Messages = messages,
            AttemptsByMessage = attempts
                .GroupBy(a => a.InboxMessageId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<InboxHandlerAttempt>)g.ToList()),
            KnownEventTypes = knownEventTypes
        };
        return View(vm);
    }

    private IQueryable<InboxMessage> BuildFilteredQuery(EventFilter filter)
    {
        IQueryable<InboxMessage> q;

        if (filter.HasJsonFilter)
        {
            q = _db.InboxMessages.FromSqlInterpolated(
                $@"SELECT * FROM inbox.InboxMessages
                   WHERE ISNULL(JSON_VALUE(Payload, {filter.JsonPath}), N'') = {filter.JsonValue}");
        }
        else
        {
            q = _db.InboxMessages.AsQueryable();
        }

        if (filter.HasEventType)
            q = q.Where(m => m.EventType == filter.EventType);

        return q;
    }
}
