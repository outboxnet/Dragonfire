using Microsoft.EntityFrameworkCore;
using InboxNet.AspNetCore;
using InboxNet.EntityFrameworkCore;
using InboxNet.EntityFrameworkCore.Extensions;
using InboxNet.Extensions;
using InboxNet.Processor.Extensions;
using InboxNet.Providers.Extensions;
using InboxNet.SampleApp.Handlers;
using InboxNet.SampleApp.Providers.Acme;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");

builder.Services
    .AddInboxNet(options =>
    {
        options.SchemaName = "inbox";
        options.BatchSize = 50;
        options.MaxConcurrentDispatch = 10;
        options.EnableOrderedProcessing = true;
    })
    .UseSqlServer(connectionString)
    .AddBackgroundDispatcher()
    .AddStripeProvider(o =>
    {
        o.SigningSecret = builder.Configuration["Webhooks:Stripe:SigningSecret"]
            ?? "whsec_replace_me";
    })
    .AddGitHubProvider(o =>
    {
        o.Secret = builder.Configuration["Webhooks:GitHub:Secret"]
            ?? "replace_me";
    })
    // Composite (validator + mapper) registration — demonstrates the Org.Front-style split
    // where signature verification and payload parsing are separate, independently testable
    // services. The pair is composed automatically behind a single IWebhookProvider.
    .AddProvider<AcmeSignatureValidator, AcmePayloadMapper>("acme")
    .AddHandler<LoggingInboxHandler>()
    .AddHandler<StripeInvoicePaidHandler>(h => h
        .ForProvider("stripe")
        .ForEvent("invoice.paid"));

var app = builder.Build();

// Ensure inbox tables exist. Demo-only — in production, use migrations
// (dotnet ef migrations add … --context InboxDbContext).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<InboxDbContext>();
    await db.Database.EnsureCreatedAsync();
}

// Health probe.
app.MapGet("/", () => Results.Ok(new { service = "InboxNet.SampleApp", status = "ok" }));

// POST /webhooks/{providerKey} — the provider is resolved by the route segment, so
// /webhooks/stripe, /webhooks/github, and /webhooks/acme all route through the same handler.
app.MapInboxWebhooks();

app.Run();
