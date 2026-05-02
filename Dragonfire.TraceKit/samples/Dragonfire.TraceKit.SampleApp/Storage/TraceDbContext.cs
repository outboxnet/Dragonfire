using Microsoft.EntityFrameworkCore;

namespace Dragonfire.TraceKit.SampleApp.Storage;

public sealed class TraceDbContext : DbContext
{
    public TraceDbContext(DbContextOptions<TraceDbContext> options) : base(options) { }

    public DbSet<TraceEntity> Traces => Set<TraceEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var trace = modelBuilder.Entity<TraceEntity>();
        trace.ToTable("ApiTraces");
        trace.HasKey(x => x.TraceId);

        trace.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
        trace.Property(x => x.Method).HasMaxLength(16).IsRequired();
        trace.Property(x => x.Url).HasMaxLength(2048).IsRequired();
        trace.Property(x => x.OperationName).HasMaxLength(256);
        trace.Property(x => x.RequestContentType).HasMaxLength(256);
        trace.Property(x => x.ResponseContentType).HasMaxLength(256);
        trace.Property(x => x.ExceptionType).HasMaxLength(512);
        trace.Property(x => x.TenantId).HasMaxLength(128);
        trace.Property(x => x.UserId).HasMaxLength(128);

        // Bodies + headers + exception text are unbounded.
        trace.Property(x => x.RequestHeadersJson).HasColumnType("nvarchar(max)").IsRequired();
        trace.Property(x => x.ResponseHeadersJson).HasColumnType("nvarchar(max)").IsRequired();
        trace.Property(x => x.RequestBody).HasColumnType("nvarchar(max)");
        trace.Property(x => x.ResponseBody).HasColumnType("nvarchar(max)");
        trace.Property(x => x.ExceptionMessage).HasColumnType("nvarchar(max)");
        trace.Property(x => x.TagsJson).HasColumnType("nvarchar(max)");

        // Index for the sessions list (newest first) and the timeline lookup.
        trace.HasIndex(x => new { x.CorrelationId, x.Sequence });
        trace.HasIndex(x => x.StartedAtUtc);
    }
}
