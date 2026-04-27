using Microsoft.EntityFrameworkCore;
using InboxNet.EntityFrameworkCore.Configurations;

namespace InboxNet.EntityFrameworkCore.Extensions;

public static class ModelBuilderExtensions
{
    /// <summary>
    /// Applies the inbox table configurations to <paramref name="modelBuilder"/> under the
    /// given <paramref name="schema"/>. Call this from the inbox <c>DbContext.OnModelCreating</c>
    /// — or from an application DbContext if co-locating inbox tables with domain tables.
    /// </summary>
    public static ModelBuilder ApplyInboxConfigurations(this ModelBuilder modelBuilder, string schema = "inbox")
    {
        modelBuilder.HasDefaultSchema(schema);
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new InboxHandlerAttemptConfiguration());
        return modelBuilder;
    }
}
