using Microsoft.EntityFrameworkCore;
using Dragonfire.Outbox.EntityFrameworkCore.Configurations;
using Dragonfire.Outbox.Models;

namespace Dragonfire.Outbox.EntityFrameworkCore;

public class OutboxDbContext : DbContext
{
    private readonly string _schema;

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<WebhookSubscription> WebhookSubscriptions => Set<WebhookSubscription>();
    public DbSet<DeliveryAttempt> DeliveryAttempts => Set<DeliveryAttempt>();

    public OutboxDbContext(DbContextOptions<OutboxDbContext> options, string schema = "outbox")
        : base(options)
    {
        _schema = schema;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(_schema);
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new WebhookSubscriptionConfiguration());
        modelBuilder.ApplyConfiguration(new DeliveryAttemptConfiguration());
    }
}
