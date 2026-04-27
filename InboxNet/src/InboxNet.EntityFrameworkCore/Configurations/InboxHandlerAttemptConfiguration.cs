using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using InboxNet.Models;

namespace InboxNet.EntityFrameworkCore.Configurations;

public class InboxHandlerAttemptConfiguration : IEntityTypeConfiguration<InboxHandlerAttempt>
{
    public void Configure(EntityTypeBuilder<InboxHandlerAttempt> builder)
    {
        builder.ToTable("InboxHandlerAttempts");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasDefaultValueSql("NEWSEQUENTIALID()");

        builder.Property(a => a.InboxMessageId).IsRequired();
        builder.Property(a => a.HandlerName).IsRequired().HasMaxLength(512);
        builder.Property(a => a.AttemptNumber).IsRequired();
        builder.Property(a => a.Status).IsRequired().HasDefaultValue(InboxHandlerStatus.Pending);
        builder.Property(a => a.DurationMs).IsRequired().HasDefaultValue(0L);
        builder.Property(a => a.ErrorMessage).HasMaxLength(4000);
        builder.Property(a => a.AttemptedAt).IsRequired().HasDefaultValueSql("SYSDATETIMEOFFSET()").HasPrecision(3);
        builder.Property(a => a.NextRetryAt).HasPrecision(3);

        builder.HasOne(a => a.InboxMessage)
            .WithMany()
            .HasForeignKey(a => a.InboxMessageId)
            .OnDelete(DeleteBehavior.Cascade);

        // GetHandlerStatesAsync lookup:
        //   WHERE InboxMessageId = @id AND HandlerName IN (...)
        //   GROUP BY HandlerName
        builder.HasIndex(a => new { a.InboxMessageId, a.HandlerName })
            .HasDatabaseName("IX_InboxHandlerAttempts_MessageId_HandlerName")
            .IncludeProperties(a => a.Status);

        // Admin queries by handler / purge.
        builder.HasIndex(a => new { a.HandlerName, a.AttemptedAt })
            .HasDatabaseName("IX_InboxHandlerAttempts_HandlerName_AttemptedAt");

        builder.HasIndex(a => a.AttemptedAt)
            .HasDatabaseName("IX_InboxHandlerAttempts_AttemptedAt");
    }
}
