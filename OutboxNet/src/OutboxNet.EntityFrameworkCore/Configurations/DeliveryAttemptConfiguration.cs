using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OutboxNet.Models;

namespace OutboxNet.EntityFrameworkCore.Configurations;

public class DeliveryAttemptConfiguration : IEntityTypeConfiguration<DeliveryAttempt>
{
    public void Configure(EntityTypeBuilder<DeliveryAttempt> builder)
    {
        builder.ToTable("DeliveryAttempts");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasDefaultValueSql("NEWSEQUENTIALID()");

        builder.Property(d => d.OutboxMessageId).IsRequired();
        builder.Property(d => d.WebhookSubscriptionId).IsRequired();
        builder.Property(d => d.AttemptNumber).IsRequired();
        builder.Property(d => d.Status).IsRequired().HasDefaultValue(DeliveryStatus.Pending);
        builder.Property(d => d.ResponseBody).HasMaxLength(4000);
        builder.Property(d => d.DurationMs).IsRequired().HasDefaultValue(0L);
        builder.Property(d => d.AttemptedAt).IsRequired().HasDefaultValueSql("SYSDATETIMEOFFSET()").HasPrecision(3);
        builder.Property(d => d.NextRetryAt).HasPrecision(3);

        builder.HasOne(d => d.OutboxMessage)
            .WithMany()
            .HasForeignKey(d => d.OutboxMessageId)
            .OnDelete(DeleteBehavior.Cascade);

        // WebhookSubscriptionId is a plain correlation column — no FK constraint.
        // Config-driven subscriptions (ConfigSubscriptionStore) are never persisted in
        // WebhookSubscriptions, so a DB-level FK would cause SaveAttemptsAsync to fail
        // for every config-based delivery, preventing MarkAsProcessed from ever running.
        builder.Property(d => d.WebhookSubscriptionId).IsRequired();

        // ── Indexes ───────────────────────────────────────────────────────────

        // GetDeliveryStatesAsync GROUP BY query:
        //   WHERE OutboxMessageId = @id AND WebhookSubscriptionId IN (...)
        //   GROUP BY WebhookSubscriptionId
        //   → Status included to avoid key-lookup for the MAX(CASE WHEN Status=1...) aggregate.
        builder.HasIndex(d => new { d.OutboxMessageId, d.WebhookSubscriptionId })
            .HasDatabaseName("IX_DeliveryAttempts_MessageId_SubscriptionId")
            .IncludeProperties(d => d.Status);

        // GetBySubscriptionIdAsync / admin queries by subscription.
        builder.HasIndex(d => new { d.WebhookSubscriptionId, d.AttemptedAt })
            .HasDatabaseName("IX_DeliveryAttempts_SubscriptionId_AttemptedAt");

        // PurgeOldAttemptsAsync: DELETE WHERE AttemptedAt < @olderThan
        builder.HasIndex(d => d.AttemptedAt)
            .HasDatabaseName("IX_DeliveryAttempts_AttemptedAt");
    }
}
