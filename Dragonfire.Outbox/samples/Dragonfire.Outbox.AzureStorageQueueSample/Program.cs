using Dragonfire.Outbox.Connectors.AzureStorageQueue.Extensions;
using Dragonfire.Outbox.Connectors.AzureStorageQueue.Options;
using Dragonfire.Outbox.AzureStorageQueueSample.Configuration;
using Dragonfire.Outbox.AzureStorageQueueSample.Consumer;
using Dragonfire.Outbox.AzureStorageQueueSample.Domain;
using Dragonfire.Outbox.EntityFrameworkCore;
using Dragonfire.Outbox.EntityFrameworkCore.Extensions;
using Dragonfire.Outbox.Extensions;
using Dragonfire.Outbox.Interfaces;
using Dragonfire.Outbox.Options;
using Dragonfire.Outbox.Processor.Extensions;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var sqlConnectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");

var queueSection = builder.Configuration.GetSection("AzureStorageQueue");
var queueConn = queueSection["ConnectionString"] ?? "UseDevelopmentStorage=true";
var queueName = queueSection["QueueName"] ?? "outbox-messages";

// 1. Host application's domain DbContext. The outbox publisher enlists in its transactions.
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(sqlConnectionString));

// 2. Outbox in QueueMediated mode. The processor calls IMessagePublisher (the AzureStorageQueue
//    connector) for each locked row instead of fanning out to webhook subscriptions.
//    This is the key switch — without it the processor falls back to direct webhook delivery
//    and the connector is never invoked.
builder.Services
    .AddOutboxNet(o =>
    {
        o.SchemaName = "outbox";
        o.BatchSize = 50;
        o.MaxConcurrentDeliveries = 8;
        o.ProcessingMode = ProcessingMode.QueueMediated;
    })
    .UseSqlServerContext<AppDbContext>(sqlConnectionString)
    .AddBackgroundProcessor()
    .UseAzureStorageQueue(o =>
    {
        o.ConnectionString = queueConn;
        o.QueueName = queueName;
        // VisibilityTimeout: how long Azure hides a message from receivers after enqueue.
        // For a transactional outbox we usually want this short — the message is durable in
        // the outbox table, so re-publishing on consumer crash is cheap.
        o.VisibilityTimeout = TimeSpan.FromSeconds(0);
        // MessageTimeToLive: messages older than this are silently purged by Azure. -1 = forever.
        o.MessageTimeToLive = TimeSpan.FromDays(7);
    });

// 3. Consumer — separate from the publisher side. In real life it would live in its own service;
//    here it shares the process so a single `dotnet run` shows the full round-trip.
builder.Services.Configure<SampleQueueConsumerOptions>(o =>
{
    o.ConnectionString = queueConn;
    o.QueueName = queueName;
});
builder.Services.AddHostedService<QueueConsumerService>();

var app = builder.Build();

// Bootstrap: create app + outbox tables on first run. Single shared SQL DB, two schemas.
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
    await scope.ServiceProvider.GetRequiredService<OutboxDbContext>().Database.EnsureCreatedAsync();
}

// 4. Producer endpoint: write an Order + outbox row in the same transaction.
//    The processor picks the row up almost immediately (push signal on publish), enqueues to
//    Azurite, and the consumer logs the JSON envelope — typically within a couple seconds end-to-end.
app.MapPost("/orders", async (
    CreateOrderRequest? input,
    AppDbContext db,
    IOutboxPublisher outbox,
    CancellationToken ct) =>
{
    var req = input ?? new CreateOrderRequest(null, null, null);

    var order = new Order
    {
        Id = Guid.NewGuid(),
        CustomerId = req.CustomerId ?? $"cust-{Random.Shared.Next(1000, 9999)}",
        Total = req.Total ?? Math.Round((decimal)Random.Shared.NextDouble() * 500m, 2),
        Currency = req.Currency ?? "USD",
        CreatedAt = DateTimeOffset.UtcNow
    };

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    db.Orders.Add(order);
    await db.SaveChangesAsync(ct);

    await outbox.PublishAsync(
        eventType: "order.created",
        payload: new
        {
            orderId = order.Id,
            customerId = order.CustomerId,
            total = order.Total,
            currency = order.Currency,
            createdAt = order.CreatedAt
        },
        correlationId: Guid.NewGuid().ToString("N"),
        entityId: order.CustomerId,
        cancellationToken: ct);

    await tx.CommitAsync(ct);

    return Results.Created($"/orders/{order.Id}", new
    {
        order.Id,
        order.CustomerId,
        order.Total,
        order.Currency
    });
});

// Convenience: GET /orders — see what's been written so you can correlate with consumer logs.
app.MapGet("/orders", async (AppDbContext db, CancellationToken ct) =>
    await db.Orders.OrderByDescending(o => o.CreatedAt).Take(50).ToListAsync(ct));

app.Run();

internal sealed record CreateOrderRequest(string? CustomerId, decimal? Total, string? Currency);
