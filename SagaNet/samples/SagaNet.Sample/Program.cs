using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SagaNet.Extensions.DependencyInjection;
using SagaNet.Persistence.EfCore;
using SagaNet.Persistence.EfCore.Queries;
using SagaNet.Persistence.EfCore.Repositories;
using SagaNet.Core.Abstractions;
using SagaNet.Sample.Workflows;
using SagaNet.Sample.Workflows.Steps;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.SetMinimumLevel(LogLevel.Debug);

// ── Persistence (InMemory for zero-setup demo; swap for UseSqlServer in prod) ─
builder.Services.AddDbContext<SagaNetDbContext>(opt =>
    opt.UseInMemoryDatabase("SagaNetSample"));

builder.Services.AddScoped<IWorkflowRepository, EfWorkflowRepository>();

// ── Register step classes (DI injects ILogger etc. into them) ────────────────
builder.Services.AddTransient<ValidateOrderStep>();
builder.Services.AddTransient<ReserveInventoryStep>();
builder.Services.AddTransient<ProcessPaymentStep>();
builder.Services.AddTransient<ShipOrderStep>();

// ── SagaNet engine ────────────────────────────────────────────────────────────
builder.Services.AddSagaNet(b => b
    .AddWorkflow<OrderWorkflow, OrderData>()
    .Configure(opt =>
    {
        opt.PollInterval           = TimeSpan.FromMilliseconds(200);
        opt.PollBatchSize          = 5;
        opt.MaxConcurrentWorkflows = 4;
    }));

// ── Task progress query service ───────────────────────────────────────────────
builder.Services.AddSagaNetQueries<EfWorkflowQueryService>();

// ── Health checks ─────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddSagaNetHealthCheck();

// ── MVC controllers + Swagger ─────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title       = "SagaNet Sample API",
        Version     = "v1",
        Description = "Trigger and inspect OrderWorkflow sagas."
    });

    // Pull XML doc comments into Swagger UI
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
        c.IncludeXmlComments(xmlPath);
});

var app = builder.Build();

// ── Ensure DB schema ──────────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<SagaNetDbContext>().Database.EnsureCreatedAsync();

// ── Swagger UI (available in all environments for the sample) ─────────────────
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "SagaNet Sample v1");
    c.RoutePrefix = string.Empty; // serve at root "/"
});

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
