using Dragonfire.Outbox.EntityFrameworkCore;
using Dragonfire.Outbox.Models;
using Dragonfire.WebhookPlatform.SampleApp.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dragonfire.WebhookPlatform.SampleApp.Controllers;

public sealed class OutgoingController : Controller
{
    private const int PageSize = 100;
    private readonly OutboxDbContext _db;

    public OutgoingController(OutboxDbContext db) => _db = db;

    // GET /Outgoing[?eventType=...&jsonPath=$.x&jsonValue=...] — top 100 outbox messages
    // matching the filter, joined to their delivery attempts.
    public async Task<IActionResult> Index([FromQuery] EventFilter filter, CancellationToken ct)
    {
        IQueryable<OutboxMessage> query = BuildFilteredQuery(filter);

        var messages = await query
            .AsNoTracking()
            .OrderByDescending(m => m.CreatedAt)
            .Take(PageSize)
            .ToListAsync(ct);

        var ids = messages.Select(m => m.Id).ToList();

        var attempts = await _db.DeliveryAttempts
            .AsNoTracking()
            .Where(a => ids.Contains(a.OutboxMessageId))
            .OrderByDescending(a => a.AttemptedAt)
            .ToListAsync(ct);

        var subIds = attempts.Select(a => a.WebhookSubscriptionId).Distinct().ToList();
        var subs = await _db.WebhookSubscriptions
            .AsNoTracking()
            .Where(s => subIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);

        var knownEventTypes = await _db.OutboxMessages
            .AsNoTracking()
            .Select(m => m.EventType)
            .Distinct()
            .OrderBy(e => e)
            .ToListAsync(ct);

        var vm = new OutgoingListViewModel
        {
            Filter = filter,
            Messages = messages,
            AttemptsByMessage = attempts
                .GroupBy(a => a.OutboxMessageId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<DeliveryAttempt>)g.ToList()),
            SubscriptionsById = subs,
            KnownEventTypes = knownEventTypes
        };
        return View(vm);
    }

    /// <summary>
    /// Composes the EF query for the chosen filter. The JSON-path branch goes through
    /// <c>FromSqlInterpolated</c> so we can call SQL Server's <c>JSON_VALUE</c> with a
    /// parameterized path — both the path and the matched value are passed as SqlParameters,
    /// keeping the query injection-safe. The schema name is hard-coded because both values
    /// are strict path/text and the schema is set in <c>OutboxOptions</c>; if you customize
    /// <c>SchemaName</c>, change the <c>FROM</c> clause here too.
    /// </summary>
    private IQueryable<OutboxMessage> BuildFilteredQuery(EventFilter filter)
    {
        IQueryable<OutboxMessage> q;

        if (filter.HasJsonFilter)
        {
            // SQL Server's JSON_VALUE accepts a variable for the path argument since 2017,
            // so we can parameterize both. ISNULL keeps numeric/null comparisons predictable.
            q = _db.OutboxMessages.FromSqlInterpolated(
                $@"SELECT * FROM outbox.OutboxMessages
                   WHERE ISNULL(JSON_VALUE(Payload, {filter.JsonPath}), N'') = {filter.JsonValue}");
        }
        else
        {
            q = _db.OutboxMessages.AsQueryable();
        }

        if (filter.HasEventType)
            q = q.Where(m => m.EventType == filter.EventType);

        return q;
    }
}
