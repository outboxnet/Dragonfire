using Dragonfire.Inbox.AspNetCore;
using Dragonfire.Inbox.EntityFrameworkCore;
using Dragonfire.Inbox.EntityFrameworkCore.Extensions;
using Dragonfire.Inbox.Extensions;
using Dragonfire.Inbox.Processor.Extensions;
using Dragonfire.Outbox.Delivery.Extensions;
using Dragonfire.Outbox.EntityFrameworkCore;
using Dragonfire.Outbox.EntityFrameworkCore.Extensions;
using Dragonfire.Outbox.Extensions;
using Dragonfire.Outbox.Interfaces;
using Dragonfire.Outbox.Models;
using Dragonfire.Outbox.Processor.Extensions;
using Dragonfire.TenantContext;
using Dragonfire.TenantContext.AspNetCore;
using Dragonfire.TenantContext.DependencyInjection;
using Dragonfire.WebhookPlatform.SampleApp.Domain;
using Dragonfire.WebhookPlatform.SampleApp.Endpoints;
using Dragonfire.WebhookPlatform.SampleApp.Inbox.Handlers;
using Dragonfire.WebhookPlatform.SampleApp.Inbox.LoopbackProvider;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");

// 1. The host application's domain DbContext. The outbox enlists in its transaction.
//    MigrationsAssembly tells EF the migration types live in this sample app, not in the
//    AppDbContext's own assembly (which here is the same — but being explicit keeps the
//    three contexts symmetrical and makes the dotnet-ef invocations consistent).
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString,
    sql => sql.MigrationsAssembly("Dragonfire.WebhookPlatform.SampleApp")));

// 2. Tenant context — resolved from X-Tenant-Id header, with a static "demo-tenant" fallback
//    so requests without the header still work end-to-end.
builder.Services
    .AddTenantContext()
    .AddHeaderResolver(o => o.HeaderName = "X-Tenant-Id")
    .AddStaticFallback(new TenantId("demo-tenant"));

// 3. Outbox — publisher writes a row in the same transaction as the user's domain write,
//    background processor picks it up and delivers to every matching WebhookSubscription.
//    We deliberately skip UseConfigWebhooks() because we need MULTIPLE delivery targets
//    (webhook.site for the outgoing demo, plus the loopback URL feeding our own inbox).
//    The EF subscription store registered by UseSqlServerContext supports any number of rows.
builder.Services
    .AddOutboxNet(o =>
    {
        o.SchemaName = "outbox";
        o.BatchSize = 50;
        o.MaxConcurrentDeliveries = 8;
    })
    .UseSqlServerContext<AppDbContext>(connectionString,
        sql => sql.MigrationsAssembly = "Dragonfire.WebhookPlatform.SampleApp")
    .AddBackgroundProcessor()
    .AddWebhookDelivery(o =>
    {
        o.HttpTimeout = TimeSpan.FromSeconds(30);
        o.Retry.MaxRetries = 3;
        o.Retry.BaseDelay = TimeSpan.FromSeconds(1);
        o.Retry.MaxDelay = TimeSpan.FromSeconds(15);
    });

// Bridge the resolved TenantContext into the outbox so every publish auto-tags the row.
builder.Services.AddScoped<IOutboxContextAccessor, TenantOutboxContextAccessor>();

// 4. Inbox — receives webhooks at /webhooks/{providerKey}. The "loopback" provider is the
//    pair (signature validator, payload mapper) that understands outbox-signed deliveries
//    coming from this same app. We also register a typed handler for "order.created".
builder.Services
    .AddInboxNet(o =>
    {
        o.SchemaName = "inbox";
        o.BatchSize = 50;
        o.MaxConcurrentDispatch = 8;
    })
    .UseSqlServer(connectionString,
        sql => sql.MigrationsAssembly = "Dragonfire.WebhookPlatform.SampleApp")
    .AddBackgroundDispatcher()
    .AddProvider<LoopbackSignatureValidator, LoopbackPayloadMapper>("loopback")
    .AddHandler<LoopbackOrderHandler>(h => h
        .ForProvider("loopback")
        .ForEvent("order.created"));

// 5. MVC for the dashboard. Controllers + Views + ViewComponents — no Razor Pages.
builder.Services.AddControllersWithViews();

var app = builder.Build();

// Bootstrap: apply migrations for all three contexts, then seed subscriptions on first run.
// All three contexts share one SQL database (DragonfireWebhookPlatformSample) but live in
// separate schemas (app / outbox / inbox), so the migrations are independent and compose
// cleanly into a single physical database. Each Migrate() call is idempotent — already-
// applied migrations are skipped after consulting __EFMigrationsHistory in its own schema.
using (var scope = app.Services.CreateScope())
{
    var sp = scope.ServiceProvider;

    var appDb = sp.GetRequiredService<AppDbContext>();
    var outboxDb = sp.GetRequiredService<OutboxDbContext>();
    var inboxDb = sp.GetRequiredService<InboxDbContext>();

    await appDb.Database.MigrateAsync();
    await outboxDb.Database.MigrateAsync();
    await inboxDb.Database.MigrateAsync();

    await SubscriptionSeeder.SeedAsync(outboxDb, app.Configuration);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseTenantContext();

app.MapDefaultControllerRoute(); // {controller=Home}/{action=Index}/{id?}

// POST /webhooks/{providerKey} — provider key matches what we registered above.
app.MapInboxWebhooks();

// /api/orders + /api/demo/burst — the JSON event sources.
app.MapOrderEndpoints();

// /ui/generate + /ui/burst — form-friendly buttons used by the dashboard sidebar.
app.MapUiHelpers();

app.Run();

// ─────────────────────────────────────────────────────────────────────────────
// Subscription seeding — copies the WebhookPlatform:Subscriptions section from
// appsettings.json into the outbox WebhookSubscriptions table on first run.
// Idempotent: skips rows whose URL already exists.
// ─────────────────────────────────────────────────────────────────────────────
internal static class SubscriptionSeeder
{
    public static async Task SeedAsync(OutboxDbContext db, IConfiguration config)
    {
        var defs = config.GetSection("WebhookPlatform:Subscriptions").Get<SubscriptionDef[]>() ?? [];
        if (defs.Length == 0) return;

        var existingUrls = await db.WebhookSubscriptions
            .Select(s => s.WebhookUrl)
            .ToListAsync();
        var existing = new HashSet<string>(existingUrls, StringComparer.OrdinalIgnoreCase);

        foreach (var def in defs)
        {
            if (string.IsNullOrWhiteSpace(def.Url) || existing.Contains(def.Url)) continue;

            db.WebhookSubscriptions.Add(new WebhookSubscription
            {
                Id = Guid.NewGuid(),
                EventType = string.IsNullOrWhiteSpace(def.EventType) ? "*" : def.EventType,
                WebhookUrl = def.Url,
                Secret = def.Secret ?? string.Empty,
                IsActive = true,
                MaxRetries = def.MaxRetries ?? 5,
                Timeout = TimeSpan.FromSeconds(def.TimeoutSeconds ?? 30),
                CreatedAt = DateTimeOffset.UtcNow,
                CustomHeaders = string.IsNullOrWhiteSpace(def.Name)
                    ? null
                    : new Dictionary<string, string> { ["X-Subscription-Name"] = def.Name! }
            });
        }

        await db.SaveChangesAsync();
    }

    private sealed class SubscriptionDef
    {
        public string? Name { get; set; }
        public string? Url { get; set; }
        public string? Secret { get; set; }
        public string? EventType { get; set; }
        public int? MaxRetries { get; set; }
        public int? TimeoutSeconds { get; set; }
    }
}
