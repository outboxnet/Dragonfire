using OutboxNet.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OutboxNet.SampleApp.Data;
using OutboxNet.EntityFrameworkCore.Extensions;
using OutboxNet.Processor.Extensions;
using OutboxNet.Delivery.Extensions;

namespace OutboxNet.SampleApp
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            
            builder.Services.AddDbContext<SampleAppContext>(options =>
                options.UseSqlServer(builder.Configuration.GetConnectionString("Default") ?? throw new InvalidOperationException("Connection string 'SampleAppContext' not found.")));
            

            // Add services to the container.
            builder.Services.AddControllersWithViews();

            var connectionString = builder.Configuration.GetConnectionString("Default");

            builder.Services.AddOutboxNet(options =>
            {
                options.SchemaName = "outbox";
                options.BatchSize = 50;
                options.DefaultVisibilityTimeout = TimeSpan.FromSeconds(60);
                options.MaxConcurrentDeliveries = 10;
            })
             .UseSqlServerContext<SampleAppContext>(connectionString!, (opt) => {
                 opt.MigrationsAssembly = "OutboxNet.SampleApp";
             })
            .AddBackgroundProcessor()
            .AddWebhookDelivery()
            .UseConfigWebhooks(builder.Configuration);
            ;

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
