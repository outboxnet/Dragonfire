using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OutboxNet.Models;

namespace OutboxNet.EntityFrameworkCore.Configurations;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasDefaultValueSql("NEWSEQUENTIALID()");

        builder.Property(m => m.EventType).IsRequired().HasMaxLength(256);
        builder.Property(m => m.Payload).IsRequired();
        builder.Property(m => m.CorrelationId).HasMaxLength(128);
        builder.Property(m => m.TraceId).HasMaxLength(128);
        builder.Property(m => m.Status).IsRequired();
        builder.Property(m => m.RetryCount).IsRequired().HasDefaultValue(0);
        builder.Property(m => m.CreatedAt).IsRequired().HasDefaultValueSql("SYSDATETIMEOFFSET()").HasPrecision(3);
        builder.Property(m => m.ProcessedAt).HasPrecision(3);
        builder.Property(m => m.LockedUntil).HasPrecision(3);
        builder.Property(m => m.LockedBy).HasMaxLength(256);
        builder.Property(m => m.NextRetryAt).HasPrecision(3);

        builder.Property(m => m.Headers).HasConversion(
            v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => v == null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions?)null));

        builder.Property(m => m.TenantId).HasMaxLength(256);
        builder.Property(m => m.UserId).HasMaxLength(256);
        builder.Property(m => m.EntityId).HasMaxLength(256);

        // ── Indexes ───────────────────────────────────────────────────────────

        // Primary index for LockNextBatchAsync CTE candidate scan:
        //   WHERE Status IN (Pending=0, Processing=1)
        //     AND (LockedUntil IS NULL OR LockedUntil < now)
        //     AND (NextRetryAt IS NULL OR NextRetryAt <= now)
        //   ORDER BY CreatedAt
        //
        // Covering columns avoid a key-lookup back to the clustered index for the
        // OUTPUT clause (Id, EventType, TenantId, UserId, EntityId are read).
        // Filtered to active statuses only (Status IN (0,1)) to keep the index small.
        builder.HasIndex(m => new { m.Status, m.CreatedAt, m.LockedUntil, m.NextRetryAt })
            .HasDatabaseName("IX_OutboxMessages_Lock_Candidate")
            .HasFilter("[Status] IN (0, 1)")
            .IncludeProperties(m => new { m.TenantId, m.UserId, m.EntityId, m.EventType, m.RetryCount });

        // ReleaseExpiredLocksAsync: WHERE Status=Processing AND LockedUntil < now
        builder.HasIndex(m => new { m.Status, m.LockedUntil })
            .HasDatabaseName("IX_OutboxMessages_Status_LockedUntil")
            .HasFilter("[LockedUntil] IS NOT NULL");

        // Ordered-processing NOT EXISTS sub-query scan: filters by Status=Processing
        // AND LockedUntil > now, partitioned by (TenantId, UserId, EntityId).
        builder.HasIndex(m => new { m.TenantId, m.UserId, m.EntityId, m.Status, m.LockedUntil })
            .HasDatabaseName("IX_OutboxMessages_PartitionKey_Status");

        // General EventType lookup (admin queries, subscription routing checks).
        builder.HasIndex(m => m.EventType)
            .HasDatabaseName("IX_OutboxMessages_EventType");
    }
}
