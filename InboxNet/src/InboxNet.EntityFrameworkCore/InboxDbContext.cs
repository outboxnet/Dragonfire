using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using InboxNet.EntityFrameworkCore.Configurations;
using InboxNet.Models;
using InboxNet.Options;

namespace InboxNet.EntityFrameworkCore;

public class InboxDbContext : DbContext
{
    private readonly string _schema;

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<InboxHandlerAttempt> InboxHandlerAttempts => Set<InboxHandlerAttempt>();

    /// <summary>
    /// Standard DI constructor. The schema is taken from <see cref="InboxOptions.SchemaName"/>
    /// so changing the option flows through to <c>HasDefaultSchema</c> as well as the raw-SQL
    /// stores. Without this, EF-managed writes (<c>SaveChanges</c>, <c>ExecuteUpdateAsync</c>)
    /// would target the default schema while raw SQL targeted the configured one.
    /// </summary>
    public InboxDbContext(DbContextOptions<InboxDbContext> options, IOptions<InboxOptions> inboxOptions)
        : base(options)
    {
        _schema = inboxOptions.Value.SchemaName;
    }

    /// <summary>
    /// Constructor for design-time tooling and tests that don't run through the inbox DI
    /// pipeline (e.g. <c>dotnet ef migrations</c>). Falls back to the default schema.
    /// </summary>
    protected InboxDbContext(DbContextOptions<InboxDbContext> options, string schema)
        : base(options)
    {
        _schema = schema;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(_schema);
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new InboxHandlerAttemptConfiguration());
    }
}
