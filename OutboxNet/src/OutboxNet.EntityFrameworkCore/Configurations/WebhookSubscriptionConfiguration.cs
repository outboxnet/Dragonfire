using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OutboxNet.Models;

namespace OutboxNet.EntityFrameworkCore.Configurations;

public class WebhookSubscriptionConfiguration : IEntityTypeConfiguration<WebhookSubscription>
{
    public void Configure(EntityTypeBuilder<WebhookSubscription> builder)
    {
        builder.ToTable("WebhookSubscriptions");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasDefaultValueSql("NEWSEQUENTIALID()");

        builder.Property(s => s.TenantId).HasMaxLength(256);
        builder.Property(s => s.EventType).IsRequired().HasMaxLength(256);
        builder.Property(s => s.WebhookUrl).IsRequired().HasMaxLength(2048);
        builder.Property(s => s.Secret).IsRequired().HasMaxLength(512);
        builder.Property(s => s.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(s => s.MaxRetries).IsRequired().HasDefaultValue(5);

        builder.Property(s => s.Timeout)
            .HasConversion(
                v => (int)v.TotalSeconds,
                v => TimeSpan.FromSeconds(v))
            .HasColumnName("TimeoutSeconds");

        builder.Property(s => s.CreatedAt).IsRequired().HasDefaultValueSql("SYSDATETIMEOFFSET()").HasPrecision(3);
        builder.Property(s => s.UpdatedAt).HasPrecision(3);

        builder.Property(s => s.CustomHeaders).HasConversion(
            v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => v == null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions?)null));

        builder.HasIndex(s => new { s.EventType, s.IsActive })
            .HasDatabaseName("IX_WebhookSubscriptions_EventType_Active");

        builder.HasIndex(s => new { s.TenantId, s.IsActive })
            .HasDatabaseName("IX_WebhookSubscriptions_TenantId_Active")
            .HasFilter("[TenantId] IS NOT NULL");
    }
}
