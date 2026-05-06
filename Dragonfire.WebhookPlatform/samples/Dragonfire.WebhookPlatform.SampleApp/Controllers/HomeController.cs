using Dragonfire.Inbox.EntityFrameworkCore;
using Dragonfire.Outbox.EntityFrameworkCore;
using Dragonfire.WebhookPlatform.SampleApp.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dragonfire.WebhookPlatform.SampleApp.Controllers;

public sealed class HomeController : Controller
{
    private readonly OutboxDbContext _outbox;
    private readonly InboxDbContext _inbox;

    public HomeController(OutboxDbContext outbox, InboxDbContext inbox)
    {
        _outbox = outbox;
        _inbox = inbox;
    }

    // GET / — overview dashboard with totals and the most recent rows from both pipelines.
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var vm = new OverviewViewModel
        {
            OutboxTotal = await _outbox.OutboxMessages.CountAsync(ct),
            InboxTotal = await _inbox.InboxMessages.CountAsync(ct),
            DeliveryAttempts = await _outbox.DeliveryAttempts.CountAsync(ct),
            Subscriptions = await _outbox.WebhookSubscriptions.CountAsync(s => s.IsActive, ct),
            RecentOutgoing = await _outbox.OutboxMessages
                .AsNoTracking()
                .OrderByDescending(m => m.CreatedAt)
                .Take(8)
                .ToListAsync(ct),
            RecentIncoming = await _inbox.InboxMessages
                .AsNoTracking()
                .OrderByDescending(m => m.ReceivedAt)
                .Take(8)
                .ToListAsync(ct)
        };
        return View(vm);
    }
}
