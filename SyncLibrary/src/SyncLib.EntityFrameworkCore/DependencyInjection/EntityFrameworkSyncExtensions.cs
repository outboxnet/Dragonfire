using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SyncLib.Abstractions;

namespace SyncLib.EntityFrameworkCore;

/// <summary>DI helpers for the EF-Core integration.</summary>
public static class EntityFrameworkSyncExtensions
{
    /// <summary>
    /// Replace the in-memory state store with an EF-Core backed one. Your
    /// <typeparamref name="TContext"/> must implement <see cref="ISyncStateDbContext"/>
    /// and call <c>modelBuilder.ConfigureSyncState()</c> in <c>OnModelCreating</c>.
    /// </summary>
    public static IServiceCollection AddEntityFrameworkSyncStateStore<TContext>(this IServiceCollection services)
        where TContext : DbContext, ISyncStateDbContext
    {
        services.AddScoped<ISyncStateStore, EfSyncStateStore<TContext>>();
        services.AddScoped<ISyncStateReader>(sp => sp.GetRequiredService<ISyncStateStore>());
        return services;
    }
}
