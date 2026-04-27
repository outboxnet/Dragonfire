using Microsoft.Extensions.DependencyInjection;
using Dragonfire.Outbox.Extensions;
using Dragonfire.Outbox.Interfaces;
using Dragonfire.Outbox.SqlServer.Stores;

namespace Dragonfire.Outbox.SqlServer.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the direct ADO.NET SQL Server stores and publisher.
    /// Uses raw <see cref="Microsoft.Data.SqlClient.SqlConnection"/> with no EF Core dependency.
    /// The publisher requires an <see cref="ISqlTransactionAccessor"/> registered by the consumer
    /// to participate in the caller's transaction.
    /// </summary>
    public static IOutboxNetBuilder UseDirectSqlServer(
        this IOutboxNetBuilder builder,
        string connectionString)
    {
        builder.Services.Configure<DirectSqlOptions>(o => o.ConnectionString = connectionString);

        builder.Services.AddScoped<IOutboxStore, DirectSqlOutboxStore>();
        builder.Services.AddScoped<DirectSqlSubscriptionStore>();
        builder.Services.AddScoped<ISubscriptionStore>(sp => sp.GetRequiredService<DirectSqlSubscriptionStore>());
        builder.Services.AddScoped<ISubscriptionReader>(sp => sp.GetRequiredService<DirectSqlSubscriptionStore>());
        builder.Services.AddScoped<IDeliveryAttemptStore, DirectSqlDeliveryAttemptStore>();
        builder.Services.AddScoped<IOutboxPublisher, DirectSqlOutboxPublisher>();

        return builder;
    }
}
