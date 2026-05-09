using Dragonfire.IdempotentApi.Builder;
using Dragonfire.IdempotentApi.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dragonfire.IdempotentApi.EntityFrameworkCore.Extensions;

public static class IdempotentApiBuilderExtensions
{
    /// <summary>
    /// Register the EF Core store using a pooled <see cref="IDbContextFactory{TContext}"/>.
    /// Caller is responsible for registering <c>AddDbContextFactory&lt;TContext&gt;</c>
    /// (or <c>AddPooledDbContextFactory</c>) before the app starts.
    /// </summary>
    /// <param name="setSelector">
    /// Returns the <see cref="DbSet{IdempotencyRecord}"/> from the user's DbContext.
    /// Defaults to <c>db => db.Set&lt;IdempotencyRecord&gt;()</c>.
    /// </param>
    public static IIdempotentApiBuilder UseEntityFrameworkCore<TContext>(
        this IIdempotentApiBuilder builder,
        Func<TContext, DbSet<IdempotencyRecord>>? setSelector = null)
        where TContext : DbContext
    {
        setSelector ??= db => db.Set<IdempotencyRecord>();

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IIdempotencyStore>(sp =>
            new EfCoreIdempotencyStore<TContext>(
                sp.GetRequiredService<IDbContextFactory<TContext>>(),
                setSelector,
                sp.GetRequiredService<TimeProvider>()));

        return builder;
    }
}
