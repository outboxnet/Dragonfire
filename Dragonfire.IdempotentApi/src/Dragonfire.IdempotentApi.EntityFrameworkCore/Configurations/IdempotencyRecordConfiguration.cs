using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dragonfire.IdempotentApi.EntityFrameworkCore.Configurations;

public sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    private readonly string _schema;
    private readonly string _tableName;

    public IdempotencyRecordConfiguration(string schema = "idempotency", string tableName = "IdempotencyRecords")
    {
        _schema = schema;
        _tableName = tableName;
    }

    public void Configure(EntityTypeBuilder<IdempotencyRecord> b)
    {
        b.ToTable(_tableName, _schema);
        b.HasKey(x => x.Key);

        b.Property(x => x.Key).HasMaxLength(256).IsRequired();
        b.Property(x => x.Fingerprint).HasMaxLength(128).IsRequired();
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.ContentType).HasMaxLength(256);
        b.Property(x => x.ResponseHeadersJson).HasColumnType("nvarchar(max)");
        b.Property(x => x.ResponseBody).HasColumnType("varbinary(max)");
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => x.ExpiresAt).HasDatabaseName("IX_IdempotencyRecords_ExpiresAt");
    }
}
