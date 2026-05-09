using Dragonfire.IdempotentApi.Extensions;
using Dragonfire.IdempotentApi.InMemory.Extensions;

var builder = WebApplication.CreateBuilder(args);

// 1. Register the suite. AddIdempotentApi binds the options. AddAspNetCore wires up the
//    HTTP-level abstractions (header reader, SHA-256 body fingerprint, response recorder,
//    method-based policy). UseInMemoryStore picks the simplest backend — swap for
//    .UseEntityFrameworkCore<MyDbContext>() in a multi-instance deployment.
builder.Services
    .AddIdempotentApi(o =>
    {
        o.HeaderName = "Idempotency-Key";
        o.DefaultExpiration = TimeSpan.FromMinutes(10);
        o.MissingKeyBehavior = Dragonfire.IdempotentApi.Options.MissingKeyBehavior.Bypass;
    })
    .AddAspNetCore()
    .UseInMemoryStore();

var app = builder.Build();

// 2. Place the middleware after routing so the attribute-based policy (if used) can see
//    the matched endpoint. Before authentication isn't required — idempotency is orthogonal.
app.UseRouting();
app.UseIdempotentApi();

// 3. Sample endpoint. Each first-time request gets a fresh order id; duplicates with the
//    same Idempotency-Key get the cached response with an "Idempotent-Replay: true" header.
var counter = 0;
app.MapPost("/orders", () =>
{
    var id = Interlocked.Increment(ref counter);
    return Results.Created($"/orders/{id}", new
    {
        id,
        createdAt = DateTimeOffset.UtcNow,
    });
});

app.MapGet("/", () => "POST /orders with header 'Idempotency-Key: <some-uuid>' to see the middleware in action.");

app.Run();
