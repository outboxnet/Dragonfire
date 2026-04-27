using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SagaNet.Persistence.EfCore;

/// <summary>
/// Design-time factory used by EF Core tooling (dotnet-ef migrations add / update).
/// It is never invoked at runtime.
///
/// Run migrations from the solution root:
/// <code>
/// dotnet ef migrations add InitialCreate \
///     --project src/SagaNet.Persistence.EfCore \
///     --startup-project src/SagaNet.Persistence.EfCore
///
/// dotnet ef database update \
///     --project src/SagaNet.Persistence.EfCore \
///     --startup-project src/SagaNet.Persistence.EfCore \
///     --connection "Server=.;Database=SagaNet;Trusted_Connection=True;"
/// </code>
/// </summary>
public sealed class SagaNetDbContextFactory : IDesignTimeDbContextFactory<SagaNetDbContext>
{
    public SagaNetDbContext CreateDbContext(string[] args)
    {
        // Connection string used only by EF tooling at design time.
        // Override via the --connection flag or SAGAET_CONNECTION env var.
        var connectionString =
            Environment.GetEnvironmentVariable("SAGANET_CONNECTION")
            ?? "Server=(localdb)\\mssqllocaldb;Database=SagaNet;Trusted_Connection=True;MultipleActiveResultSets=true";

        var options = new DbContextOptionsBuilder<SagaNetDbContext>()
            .UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(SagaNetDbContext).Assembly.FullName);
                sql.EnableRetryOnFailure(3);
            })
            .Options;

        return new SagaNetDbContext(options);
    }
}
