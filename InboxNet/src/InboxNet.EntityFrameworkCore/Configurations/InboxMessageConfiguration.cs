using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using InboxNet.Models;

namespace InboxNet.EntityFrameworkCore.Configurations;

public class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.ToTable("InboxMessages");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasDefaultValueSql("NEWSEQUENTIALID()");

        builder.Property(m => m.ProviderKey).IsRequired().HasMaxLength(64);
        builder.Property(m => m.EventType).IsRequired().HasMaxLength(256);
        builder.Property(m => m.Payload).IsRequired();
        builder.Property(m => m.ContentSha256).IsRequired().HasMaxLength(64);
        builder.Property(m => m.ProviderEventId).HasMaxLength(256);
        builder.Property(m => m.DedupKey).IsRequired().HasMaxLength(256);

        builder.Property(m => m.Status).IsRequired();
        builder.Property(m => m.RetryCount).IsRequired().HasDefaultValue(0);
        builder.Property(m => m.ReceivedAt).IsRequired().HasDefaultValueSql("SYSDATETIMEOFFSET()").HasPrecision(3);
        builder.Property(m => m.ProcessedAt).HasPrecision(3);
        builder.Property(m => m.LockedUntil).HasPrecision(3);
        builder.Property(m => m.LockedBy).HasMaxLength(256);
        builder.Property(m => m.NextRetryAt).HasPrecision(3);

        builder.Property(m => m.CorrelationId).HasMaxLength(128);
        builder.Property(m => m.TraceId).HasMaxLength(128);

        // Headers have write-once semantics: the receive endpoint builds the dictionary,
        // hands it to the publisher, and nothing in the dispatch pipeline mutates it after
        // persistence (status updates go through ExecuteUpdateAsync and don't touch Headers).
        // That lets us short-circuit the comparer to reference equality and skip the
        // defensive snapshot copy — saving an O(N) Dict.Except per change check and an
        // allocation per change-tracker snapshot.
        var headersComparer = new ValueComparer<Dictionary<string, string>?>(
            (a, b) => ReferenceEquals(a, b),
            v => v == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(v),
            v => v);

        builder.Property(m => m.Headers)
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => v == null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions?)null))
            .Metadata.SetValueComparer(headersComparer);

        builder.Property(m => m.TenantId).HasMaxLength(256);
        builder.Property(m => m.EntityId).HasMaxLength(256);

        // ── Indexes ───────────────────────────────────────────────────────────

        // Dedup constraint: (ProviderKey, DedupKey) is globally unique.
        // DedupKey = ProviderEventId when available, otherwise ContentSha256.
        // A duplicate insert throws SqlException with error 2601/2627 which the
        // store converts into an IsDuplicate = true result.
        builder.HasIndex(m => new { m.ProviderKey, m.DedupKey })
            .IsUnique()
            .HasDatabaseName("UX_InboxMessages_ProviderKey_DedupKey");

        // Primary index for LockNextBatchAsync candidate scan:
        //   WHERE Status IN (Pending=0, Processing=1)
        //     AND (LockedUntil IS NULL OR LockedUntil < now)
        //     AND (NextRetryAt IS NULL OR NextRetryAt <= now)
        //   ORDER BY ReceivedAt
        builder.HasIndex(m => new { m.Status, m.ReceivedAt, m.LockedUntil, m.NextRetryAt })
            .HasDatabaseName("IX_InboxMessages_Lock_Candidate")
            .HasFilter("[Status] IN (0, 1)")
            .IncludeProperties(m => new { m.TenantId, m.ProviderKey, m.EntityId, m.EventType, m.RetryCount });

        // ReleaseExpiredLocksAsync: WHERE Status=Processing AND LockedUntil < now
        builder.HasIndex(m => new { m.Status, m.LockedUntil })
            .HasDatabaseName("IX_InboxMessages_Status_LockedUntil")
            .HasFilter("[LockedUntil] IS NOT NULL");

        // Ordered-dispatch NOT EXISTS sub-query scan: filters by Status=Processing
        // AND LockedUntil > now, partitioned by (TenantId, ProviderKey, EntityId).
        builder.HasIndex(m => new { m.TenantId, m.ProviderKey, m.EntityId, m.Status, m.LockedUntil })
            .HasDatabaseName("IX_InboxMessages_PartitionKey_Status");

        // Lookup by provider+event (admin queries, metrics dashboards).
        builder.HasIndex(m => new { m.ProviderKey, m.EventType })
            .HasDatabaseName("IX_InboxMessages_ProviderKey_EventType");
    }
}
