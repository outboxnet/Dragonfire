using CacheTesting.Service;
using Dragonfire.Caching.Extensions;
using Dragonfire.Caching.Memory.Extensions;

namespace SampleApp
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddControllersWithViews();

            builder.Services.AddScoped<IDataService, DataService>();

            builder.Services
            .AddDragonfireMemoryCache()
            .AddDragonfireCaching(builder.Configuration, configure: b =>
                b.UseQueuedInvalidation(o =>
                {
                    o.Capacity = 10_000;
                    o.DropWhenFull = false;
                    o.ConsumerCount = 2;
                }));

            builder.Services.AddDragonFireCachedService<IDataService, DataService>();


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
