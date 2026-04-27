using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using InboxNet.EntityFrameworkCore.Stores;
using InboxNet.Extensions;
using InboxNet.Interfaces;

namespace InboxNet.EntityFrameworkCore.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the EF Core–backed SQL Server stores and publisher for the inbox. A dedicated
    /// <see cref="InboxDbContext"/> owns the inbox tables — receive endpoints use it directly
    /// and do not need to share a transaction with the caller's domain DbContext, since
    /// inbox ingestion is the atomic unit.
    /// </summary>
    public static IInboxNetBuilder UseSqlServer(
        this IInboxNetBuilder builder,
        string connectionString,
        Action<EfCoreInboxSqlServerOptions>? configure = null)
    {
        var sqlOptions = new EfCoreInboxSqlServerOptions();
        configure?.Invoke(sqlOptions);

        builder.Services.AddDbContext<InboxDbContext>(options =>
        {
            options.UseSqlServer(connectionString, sql =>
            {
                if (sqlOptions.MigrationsAssembly is not null)
                    sql.MigrationsAssembly(sqlOptions.MigrationsAssembly);

                // EnableRetryOnFailure transparently retries SQL Server deadlocks (1205),
                // connection drops, and other transient errors. Strongly recommended at
                // dispatcher concurrency above ~50 — concurrent batch-lock UPDATEs and
                // receive-side INSERTs can deadlock on the candidate index.
                if (sqlOptions.EnableRetryOnFailure)
                {
                    sql.EnableRetryOnFailure(
                        maxRetryCount: sqlOptions.MaxRetryCount,
                        maxRetryDelay: sqlOptions.MaxRetryDelay,
                        errorNumbersToAdd: null);
                }
            });
        });

        builder.Services.AddScoped<IInboxStore, EfCoreInboxStore>();
        builder.Services.AddScoped<IInboxHandlerAttemptStore, EfCoreInboxHandlerAttemptStore>();
        builder.Services.AddScoped<IInboxPublisher, EfCoreInboxPublisher>();

        return builder;
    }
}

public class EfCoreInboxSqlServerOptions
{
    public string? MigrationsAssembly { get; set; }

    /// <summary>
    /// Enables EF Core's built-in transient-error retry. Catches SQL Server deadlocks
    /// (error 1205) plus the standard set of connection / availability errors.
    /// Default: <c>true</c>.
    /// </summary>
    public bool EnableRetryOnFailure { get; set; } = true;

    /// <summary>Maximum retry attempts when <see cref="EnableRetryOnFailure"/> is true. Default: 5.</summary>
    public int MaxRetryCount { get; set; } = 5;

    /// <summary>Maximum delay between retries (exponential backoff up to this cap). Default: 5s.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(5);
}
