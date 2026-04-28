using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dragonfire.Features.Configuration;

/// <summary>
/// DI extensions that register a <see cref="ConfigurationFeatureSource"/>. Add this on top of
/// <c>AddDragonfireFeatures()</c>.
/// </summary>
public static class DragonfireFeaturesConfigurationExtensions
{
    public static IServiceCollection AddDragonfireFeaturesConfiguration(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = "Features")
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));

        services.AddSingleton<IFeatureSource>(_ => new ConfigurationFeatureSource(configuration, sectionName));
        return services;
    }
}
