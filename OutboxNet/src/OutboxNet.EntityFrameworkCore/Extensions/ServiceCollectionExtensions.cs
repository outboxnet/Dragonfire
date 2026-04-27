using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OutboxNet.EntityFrameworkCore.Stores;
using OutboxNet.Extensions;
using OutboxNet.Interfaces;

namespace OutboxNet.EntityFrameworkCore.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the EF Core–backed SQL Server stores and publisher.
    /// Uses <see cref="OutboxDbContext"/> for data access. The publisher enlists in the
    /// active transaction of your <typeparamref name="TDbContext"/> to guarantee atomicity
    /// between domain writes and outbox inserts.
    /// </summary>
    public static IOutboxNetBuilder UseSqlServerContext<TDbContext>(
        this IOutboxNetBuilder builder,
        string connectionString,
        Action<EfCoreSqlServerOptions>? configure = null)
        where TDbContext : DbContext
    {
        var sqlOptions = new EfCoreSqlServerOptions();
        configure?.Invoke(sqlOptions);

        builder.Services.AddDbContext<OutboxDbContext>(options =>
        {
            options.UseSqlServer(connectionString, sql =>
            {
                if (sqlOptions.MigrationsAssembly is not null)
                    sql.MigrationsAssembly(sqlOptions.MigrationsAssembly);
            });
        });

        builder.Services.AddScoped<IOutboxStore, EfCoreOutboxStore>();
        builder.Services.AddScoped<EfCoreSubscriptionStore>();
        builder.Services.AddScoped<ISubscriptionStore>(sp => sp.GetRequiredService<EfCoreSubscriptionStore>());
        builder.Services.AddScoped<ISubscriptionReader>(sp => sp.GetRequiredService<EfCoreSubscriptionStore>());
        builder.Services.AddScoped<IDeliveryAttemptStore, EfCoreDeliveryAttemptStore>();
        builder.Services.AddScoped<IOutboxPublisher, EfCoreOutboxPublisher<TDbContext>>();

        return builder;
    }
}

public class EfCoreSqlServerOptions
{
    public string? MigrationsAssembly { get; set; }
}
