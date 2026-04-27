using Microsoft.EntityFrameworkCore;
using OutboxNet.EntityFrameworkCore.Configurations;

namespace OutboxNet.EntityFrameworkCore.Extensions;

public static class ModelBuilderExtensions
{
    public static ModelBuilder ApplyOutboxConfigurations(this ModelBuilder modelBuilder, string schema = "outbox")
    {
        modelBuilder.HasDefaultSchema(schema);
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new WebhookSubscriptionConfiguration());
        modelBuilder.ApplyConfiguration(new DeliveryAttemptConfiguration());
        return modelBuilder;
    }
}
