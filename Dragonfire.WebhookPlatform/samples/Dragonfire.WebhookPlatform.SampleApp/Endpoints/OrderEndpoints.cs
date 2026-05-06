using Dragonfire.Outbox.Interfaces;
using Dragonfire.TenantContext;
using Dragonfire.WebhookPlatform.SampleApp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Dragonfire.WebhookPlatform.SampleApp.Endpoints;

public static class OrderEndpoints
{
    public static void MapOrderEndpoints(this IEndpointRouteBuilder routes)
    {
        // POST /api/orders — creates an order and atomically enqueues an "order.created"
        // outbox event. The background processor delivers it to every matching subscription.
        // The X-Tenant-Id header is required so TenantContext middleware tags the event.
        routes.MapPost("/api/orders", async (
            CreateOrderRequest? input,
            AppDbContext db,
            IOutboxPublisher outbox,
            ITenantContextAccessor tenantAccessor,
            CancellationToken ct) =>
        {
            var tenant = tenantAccessor.Current;
            if (!tenant.IsResolved)
                return Results.BadRequest(new { error = "X-Tenant-Id header is required." });

            var req = input ?? new CreateOrderRequest(null, null, null);

            var order = new Order
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.TenantId.Value,
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
                    tenantId = order.TenantId,
                    customerId = order.CustomerId,
                    total = order.Total,
                    currency = order.Currency,
                    createdAt = order.CreatedAt
                },
                correlationId: Guid.NewGuid().ToString("N"),
                entityId: order.CustomerId,
                cancellationToken: ct);

            await tx.CommitAsync(ct);

            return Results.Created($"/api/orders/{order.Id}", new
            {
                order.Id,
                order.TenantId,
                order.CustomerId,
                order.Total,
                order.Currency
            });
        });

        // Convenience: POST /api/demo/burst?n=5 — generates N orders in one go so the
        // dashboard fills up quickly when you hit it for the first time.
        routes.MapPost("/api/demo/burst", async (
            int? n,
            AppDbContext db,
            IOutboxPublisher outbox,
            ITenantContextAccessor tenantAccessor,
            CancellationToken ct) =>
        {
            var tenant = tenantAccessor.Current;
            if (!tenant.IsResolved)
                return Results.BadRequest(new { error = "X-Tenant-Id header is required." });

            var count =  500;
            var ids = new List<Guid>(count);

            for (var i = 0; i < count; i++)
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);

                var order = new Order
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenant.TenantId.Value,
                    CustomerId = $"cust-{Random.Shared.Next(1000, 9999)}",
                    Total = Math.Round((decimal)Random.Shared.NextDouble() * 500m, 2),
                    Currency = "USD",
                    CreatedAt = DateTimeOffset.UtcNow
                };

                db.Orders.Add(order);
                await db.SaveChangesAsync(ct);

                await outbox.PublishAsync(
                    eventType: "order.created",
                    payload: new
                    {
                        orderId = order.Id,
                        tenantId = order.TenantId,
                        customerId = order.CustomerId,
                        total = order.Total,
                        currency = order.Currency,
                        createdAt = order.CreatedAt
                    },
                    correlationId: Guid.NewGuid().ToString("N"),
                    entityId: order.CustomerId,
                    cancellationToken: ct);

                await tx.CommitAsync(ct);
                ids.Add(order.Id);
            }

            return Results.Ok(new { created = ids.Count, ids });
        });
    }

    public sealed record CreateOrderRequest(string? CustomerId, decimal? Total, string? Currency);

    /// <summary>
    /// Form-friendly UI buttons. The browser posts <c>application/x-www-form-urlencoded</c>
    /// with no body, the endpoint runs the same publish flow used by /api/orders, and we
    /// 303-redirect back to the page the user came from. Antiforgery is off for these so
    /// they work as a plain &lt;form&gt; with no token.
    /// </summary>
    public static void MapUiHelpers(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/ui/generate", async (HttpContext http,
            AppDbContext db, IOutboxPublisher outbox, ITenantContextAccessor tenantAccessor,
            CancellationToken ct) =>
        {
            await PublishOneAsync(db, outbox, tenantAccessor, ct);
            return RedirectBack(http);
        }).DisableAntiforgery();

        routes.MapPost("/ui/burst", async (int? n, HttpContext http,
            AppDbContext db, IOutboxPublisher outbox, ITenantContextAccessor tenantAccessor,
            CancellationToken ct) =>
        {
            var count = Math.Clamp(n ?? 5, 1, 50);
            for (var i = 0; i < count; i++)
                await PublishOneAsync(db, outbox, tenantAccessor, ct);
            return RedirectBack(http);
        }).DisableAntiforgery();
    }

    private static async Task PublishOneAsync(
        AppDbContext db, IOutboxPublisher outbox, ITenantContextAccessor tenantAccessor,
        CancellationToken ct)
    {
        var tenant = tenantAccessor.Current;
        var tenantId = tenant.IsResolved ? tenant.TenantId.Value : "demo-tenant";

        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomerId = $"cust-{Random.Shared.Next(1000, 9999)}",
            Total = Math.Round((decimal)Random.Shared.NextDouble() * 500m, 2),
            Currency = "USD",
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
                tenantId = order.TenantId,
                customerId = order.CustomerId,
                total = order.Total,
                currency = order.Currency,
                createdAt = order.CreatedAt
            },
            correlationId: Guid.NewGuid().ToString("N"),
            entityId: order.CustomerId,
            cancellationToken: ct);

        await tx.CommitAsync(ct);
    }

    private static IResult RedirectBack(HttpContext http)
    {
        var referer = http.Request.Headers.Referer.ToString();
        return Results.Redirect(string.IsNullOrEmpty(referer) ? "/Outgoing" : referer);
    }
}
