using System.Reflection;
using Poller.Models;
using Poller.Sample.WeatherApi.Weather;
using Poller.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Poller framework ──────────────────────────────────────────────────────────
builder.Services.AddPollingService<WeatherPollingRequest, WeatherPollingResponse>(options =>
{
    options.MaxConcurrentPollings = 50;
    options.QueueCapacity         = 1_000;
    options.DataRetentionPeriod   = TimeSpan.FromHours(1);
});

builder.Services.AddScoped<IPollingStrategy<WeatherPollingRequest, WeatherPollingResponse>, WeatherPollingStrategy>();
builder.Services.AddScoped<IPollingCondition<WeatherPollingResponse>, WeatherPollingCondition>();
builder.Services.AddHttpClient<WeatherPollingStrategy>();

// ── Web API + Swagger ─────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title       = "Poller — Weather Sample API",
        Version     = "v1",
        Description =
            "Demonstrates the **Poller** NuGet package against the free [Open-Meteo](https://open-meteo.com) " +
            "weather API (no API key required).\n\n" +
            "**Typical flow**\n" +
            "1. `POST /api/weather` → receive a `pollingId`\n" +
            "2. `GET /api/weather/{pollingId}` → poll until `status` is `Completed`\n" +
            "3. Read `weather` from the response body"
    });

    // Include XML doc comments from this assembly in Swagger UI
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
        c.IncludeXmlComments(xmlPath);
});

var app = builder.Build();

// Always enable Swagger (this is a sample project)
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Poller Weather Sample v1");
    c.RoutePrefix = string.Empty;   // Serve Swagger UI at the root URL
    c.DocumentTitle = "Poller Weather API";
    c.DisplayRequestDuration();
    c.EnableTryItOutByDefault();    // All operations start in "try it out" mode
});

app.UseHttpsRedirection();
app.MapControllers();
app.Run();
