using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dragonfire.Features.EntityFrameworkCore;

/// <summary>
/// DI extensions registering <see cref="EfCoreFeatureSource"/> and <see cref="EfCoreFeatureAuditLog"/>.
/// Pass the closed type of your application DbContext that implements <see cref="IFeaturesDbContext"/>:
///
/// <code>
/// builder.Services.AddDbContext&lt;AppDbContext&gt;(o =&gt; o.UseSqlServer(connStr));
/// builder.Services.AddDragonfireFeatures();
/// builder.Services.AddDragonfireFeaturesEntityFrameworkCore&lt;AppDbContext&gt;();
/// </code>
/// </summary>
public static class DragonfireFeaturesEntityFrameworkCoreExtensions
{
    public static IServiceCollection AddDragonfireFeaturesEntityFrameworkCore<TDbContext>(
        this IServiceCollection services)
        where TDbContext : DbContext, IFeaturesDbContext
    {
        if (services is null) throw new ArgumentNullException(nameof(services));

        // Forward IFeaturesDbContext to the registered TDbContext so source + audit log
        // never see the concrete type.
        services.AddScoped<IFeaturesDbContext>(sp => sp.GetRequiredService<TDbContext>());

        services.AddSingleton<IFeatureSource, EfCoreFeatureSource>();
        services.Replace(ServiceDescriptor.Singleton<IFeatureAuditLog, EfCoreFeatureAuditLog>());

        return services;
    }
}
