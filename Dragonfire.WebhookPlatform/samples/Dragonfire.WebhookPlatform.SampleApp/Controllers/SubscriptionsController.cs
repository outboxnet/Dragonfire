using Dragonfire.Outbox.EntityFrameworkCore;
using Dragonfire.Outbox.Models;
using Dragonfire.WebhookPlatform.SampleApp.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dragonfire.WebhookPlatform.SampleApp.Controllers;

public sealed class SubscriptionsController : Controller
{
    private readonly OutboxDbContext _db;

    public SubscriptionsController(OutboxDbContext db) => _db = db;

    // GET /Subscriptions
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var subs = await _db.WebhookSubscriptions
            .AsNoTracking()
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);
        return View(subs);
    }

    // GET /Subscriptions/Create
    [HttpGet]
    public IActionResult Create() => View(new SubscriptionFormModel());

    // POST /Subscriptions/Create
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SubscriptionFormModel form, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return View(form);

        var entity = new WebhookSubscription
        {
            Id = Guid.NewGuid(),
            EventType = string.IsNullOrWhiteSpace(form.EventType) ? "*" : form.EventType.Trim(),
            WebhookUrl = form.WebhookUrl.Trim(),
            Secret = form.Secret,
            TenantId = string.IsNullOrWhiteSpace(form.TenantId) ? null : form.TenantId.Trim(),
            IsActive = form.IsActive,
            MaxRetries = form.MaxRetries,
            Timeout = TimeSpan.FromSeconds(form.TimeoutSeconds),
            CreatedAt = DateTimeOffset.UtcNow,
            CustomHeaders = string.IsNullOrWhiteSpace(form.Name)
                ? null
                : new Dictionary<string, string> { ["X-Subscription-Name"] = form.Name.Trim() }
        };

        _db.WebhookSubscriptions.Add(entity);
        await _db.SaveChangesAsync(ct);

        TempData["Toast"] = $"Subscription created: {entity.WebhookUrl}";
        return RedirectToAction(nameof(Index));
    }

    // GET /Subscriptions/Edit/{id}
    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var s = await _db.WebhookSubscriptions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return NotFound();

        return View(new SubscriptionFormModel
        {
            Id = s.Id,
            Name = s.CustomHeaders is { } h && h.TryGetValue("X-Subscription-Name", out var n) ? n : null,
            WebhookUrl = s.WebhookUrl,
            EventType = s.EventType,
            Secret = s.Secret,
            TenantId = s.TenantId,
            MaxRetries = s.MaxRetries,
            TimeoutSeconds = (int)s.Timeout.TotalSeconds,
            IsActive = s.IsActive
        });
    }

    // POST /Subscriptions/Edit/{id}
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, SubscriptionFormModel form, CancellationToken ct)
    {
        if (id != form.Id) return BadRequest();
        if (!ModelState.IsValid) return View(form);

        var entity = await _db.WebhookSubscriptions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return NotFound();

        entity.EventType = string.IsNullOrWhiteSpace(form.EventType) ? "*" : form.EventType.Trim();
        entity.WebhookUrl = form.WebhookUrl.Trim();
        entity.Secret = form.Secret;
        entity.TenantId = string.IsNullOrWhiteSpace(form.TenantId) ? null : form.TenantId.Trim();
        entity.IsActive = form.IsActive;
        entity.MaxRetries = form.MaxRetries;
        entity.Timeout = TimeSpan.FromSeconds(form.TimeoutSeconds);
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.CustomHeaders = string.IsNullOrWhiteSpace(form.Name)
            ? null
            : new Dictionary<string, string> { ["X-Subscription-Name"] = form.Name.Trim() };

        await _db.SaveChangesAsync(ct);

        TempData["Toast"] = "Subscription updated.";
        return RedirectToAction(nameof(Index));
    }

    // POST /Subscriptions/ToggleActive/{id} — single-click activate/deactivate.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(Guid id, CancellationToken ct)
    {
        var entity = await _db.WebhookSubscriptions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return NotFound();
        entity.IsActive = !entity.IsActive;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        TempData["Toast"] = entity.IsActive ? "Subscription activated." : "Subscription deactivated.";
        return RedirectToAction(nameof(Index));
    }

    // POST /Subscriptions/Delete/{id}
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var entity = await _db.WebhookSubscriptions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return NotFound();

        _db.WebhookSubscriptions.Remove(entity);
        await _db.SaveChangesAsync(ct);

        TempData["Toast"] = "Subscription deleted.";
        return RedirectToAction(nameof(Index));
    }
}
