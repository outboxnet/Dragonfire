using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Acme.BillingClient;

public static class BillingServiceCollectionExtensions
{
    public static IServiceCollection AddBillingClient(
        this IServiceCollection services,
        Action<BillingClientOptions>? configureClient = null,
        Action<BillingLoggingOptions>? configureLogging = null)
    {
        if (configureClient  is not null) services.Configure(configureClient);
        else                              services.AddOptions<BillingClientOptions>();

        if (configureLogging is not null) services.Configure(configureLogging);
        else                              services.AddOptions<BillingLoggingOptions>();

        services.TryAddSingleton<IBillingRequestSigner, NoOpBillingRequestSigner>();
        services.TryAddSingleton<IBillingHttpLogger, DefaultBillingHttpLogger>();
        services.TryAddSingleton<IBillingErrorHandler, NoOpBillingErrorHandler>();

        services.AddHttpClient<IBillingClient, BillingClient>((sp, http) =>
        {
            var opt = sp.GetRequiredService<IOptions<BillingClientOptions>>().Value;
            if (!string.IsNullOrEmpty(opt.BaseUrl))
                http.BaseAddress = new Uri(opt.BaseUrl);
            http.Timeout = opt.Timeout;
        });

        return services;
    }
}
