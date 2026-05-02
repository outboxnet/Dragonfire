using Dragonfire.TraceKit.Abstractions;
using Dragonfire.TraceKit.AspNetCore.Extensions;
using Dragonfire.TraceKit.Extensions;
using Dragonfire.TraceKit.SampleApp.Clients;
using Dragonfire.TraceKit.SampleApp.Storage;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// SQL Server LocalDB — the .mdf lives under the user profile
// (%USERPROFILE%\Dragonfire_TraceKit_Sample.mdf) and is created on first run by
// EnsureCreated() below. No migrations are required for the sample.
var connectionString = builder.Configuration.GetConnectionString("TraceKit")
    ?? @"Server=(localdb)\MSSQLLocalDB;Database=Dragonfire_TraceKit_Sample;Integrated Security=true;MultipleActiveResultSets=true;TrustServerCertificate=true";

builder.Services.AddDbContext<TraceDbContext>(opts => opts.UseSqlServer(connectionString));

// EfCoreTraceRepository implements both ITraceRepository (write — invoked by TraceKit's
// background drain inside its own DI scope) and ITraceQuery (read — used by the MVC
// viewer). Scoped so each call gets a fresh DbContext.
builder.Services.AddScoped<EfCoreTraceRepository>();
builder.Services.AddScoped<ITraceRepository>(sp => sp.GetRequiredService<EfCoreTraceRepository>());
builder.Services.AddScoped<ITraceQuery>(sp => sp.GetRequiredService<EfCoreTraceRepository>());

builder.Services
    .AddTraceKitForAspNetCore(opts =>
    {
        opts.MaxBodyBytes = 32 * 1024;
        opts.Redaction.SensitiveJsonProperties.Add("apiKey");
        opts.Redaction.SensitiveHeaders.Add("X-Tenant-Secret");
    });

// Typed HttpClients — TraceKit auto-attaches its DelegatingHandler to ALL clients via
// IHttpMessageHandlerBuilderFilter, so no per-client configuration is needed here.
builder.Services.AddHttpClient<JsonPlaceholderClient>(c =>
{
    c.BaseAddress = new Uri("https://jsonplaceholder.typicode.com/");
    c.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddHttpClient<CatFactsClient>(c =>
{
    c.BaseAddress = new Uri("https://catfact.ninja/");
    c.Timeout = TimeSpan.FromSeconds(15);
});

var app = builder.Build();

// Bring the schema into existence on first start. Production hosts should use migrations.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TraceDbContext>();
    db.Database.EnsureCreated();
}

app.UseTraceKit();   // place early — wraps the entire downstream pipeline.

app.UseStaticFiles();
app.UseRouting();

app.MapControllers();
app.MapDefaultControllerRoute();

app.Run();
