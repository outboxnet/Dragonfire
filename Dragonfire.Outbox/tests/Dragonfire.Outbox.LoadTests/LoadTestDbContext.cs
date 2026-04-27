using Microsoft.EntityFrameworkCore;

namespace Dragonfire.Outbox.LoadTests;

/// <summary>
/// Minimal DbContext with no application entities.
/// Its sole purpose is to open a SQL Server connection and start a transaction
/// that <see cref="Dragonfire.Outbox.EntityFrameworkCore.EfCoreOutboxPublisher{TDbContext}"/> enlists
/// in — keeping the outbox INSERT atomic with the (empty) domain write in the load test.
/// </summary>
public sealed class LoadTestDbContext : DbContext
{
    public LoadTestDbContext(DbContextOptions<LoadTestDbContext> options) : base(options) { }
}
