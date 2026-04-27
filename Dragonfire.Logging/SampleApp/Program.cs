using Dragonfire.Logging.AspNetCore.Extensions;
using Dragonfire.Logging.Generated;
using SampleApp.Services;
using Dragonfire.Logging.Extensions;

namespace SampleApp
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddControllersWithViews();

            builder.Services.AddDragonfireAspNetCore(
                core: opt =>
                {
                    opt.DefaultMaxDepth = 1;
                    opt.DefaultMaxContentLength = 10_000;
                    opt.IncludeStackTraceOnError = true;
                },
                http: opt =>
                {
                    opt.EnableRequestLogging = true;
                    opt.EnableResponseLogging = true;
                    opt.ExcludePaths = new[] { "/health", "/metrics", "/swagger" };
                });

            builder.Services.AddScoped<IOrderService, OrderService>();   // OrderService : ILoggable

            builder.Logging.AddJsonConsole(o =>
            {
                o.IncludeScopes = true;
                o.JsonWriterOptions = new System.Text.Json.JsonWriterOptions { Indented = true };
            });

            //    The configure lambda is optional — omit it to use all defaults.
            builder.Services.AddDragonfireGeneratedLogging(options =>
            {
                options.LogNullResponse = true;
                options.LogRequestProperties = true;   // include Request.* fields (default: true)
                options.LogResponseProperties = true;   // include Response.* fields (default: true)
                options.LogStackTrace = false;  // omit Dragonfire.StackTrace (default: true)
                options.OverrideLevel = null;   // null = use [Log(Level=...)] per method

                // Suppress specific fields by bare name (matches both Request.X and Response.X)
                options.ExcludeProperties.Add("InternalId");
                options.ExcludeProperties.Add("RawPayload");
            });

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            app.Run();
        }
    }
}
