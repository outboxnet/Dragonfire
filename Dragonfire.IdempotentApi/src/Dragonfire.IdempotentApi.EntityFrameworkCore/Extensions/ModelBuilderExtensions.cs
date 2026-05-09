using Dragonfire.IdempotentApi.EntityFrameworkCore.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Dragonfire.IdempotentApi.EntityFrameworkCore.Extensions;

public static class ModelBuilderExtensions
{
    /// <summary>
    /// Apply the idempotency-record entity configuration to your <see cref="DbContext"/>.
    /// Call from <c>OnModelCreating</c> alongside your own configurations.
    /// </summary>
    public static ModelBuilder ApplyIdempotentApiConfigurations(
        this ModelBuilder modelBuilder,
        string schema = "idempotency",
        string tableName = "IdempotencyRecords")
    {
        modelBuilder.ApplyConfiguration(new IdempotencyRecordConfiguration(schema, tableName));
        return modelBuilder;
    }
}
