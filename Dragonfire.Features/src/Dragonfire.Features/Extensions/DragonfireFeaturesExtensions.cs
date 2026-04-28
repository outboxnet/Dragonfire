using System;
using Dragonfire.Features.Audit;
using Dragonfire.Features.Internal;
using Dragonfire.Features.Refresh;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Dragonfire.Features;

/// <summary>
/// Entry-point DI extensions for <c>Dragonfire.Features</c>. Registers the in-memory store,
/// default resolver, no-op audit log, and the periodic refresh hosted service.
/// Add at least one <see cref="IFeatureSource"/> (configuration, EF Core, custom) — without
/// one the store stays empty and every feature resolves to <c>false</c>.
///
/// <code>
/// builder.Services.AddDragonfireFeatures(o =>
/// {
///     o.RefreshInterval = TimeSpan.FromSeconds(15);
/// });
/// builder.Services.AddDragonfireFeaturesConfiguration(builder.Configuration);
/// </code>
/// </summary>
public static class DragonfireFeaturesExtensions
{
    public static IServiceCollection AddDragonfireFeatures(
        this IServiceCollection services,
        Action<FeatureRefreshOptions>? configure = null)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));

        if (configure is not null)
            services.Configure(configure);
        else
            services.AddOptions<FeatureRefreshOptions>();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IFeatureStore, InMemoryFeatureStore>();
        services.TryAddSingleton<IFeatureContextAccessor, DefaultFeatureContextAccessor>();
        services.TryAddSingleton<IFeatureResolver, DefaultFeatureResolver>();
        services.TryAddSingleton<IFeatureAuditLog, NoOpFeatureAuditLog>();
        services.AddHostedService<FeatureRefreshHostedService>();

        return services;
    }
}
