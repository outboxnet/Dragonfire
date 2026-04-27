using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Poller.Services;

namespace Poller.Models
{
    /// <summary>
    /// DI registration helpers for the Poller framework.
    /// </summary>
    public static class PollingServiceRegistration
    {
        /// <summary>
        /// Registers all polling infrastructure and a typed polling service for
        /// the <typeparamref name="TRequest"/> / <typeparamref name="TResponse"/> pair.
        ///
        /// Safe to call multiple times for different type pairs; shared singletons
        /// (orchestrator, registry, metrics tracker) are only registered once.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configure">Optional callback to customise <see cref="PollingServiceConfiguration"/>.</param>
        public static IServiceCollection AddPollingService<TRequest, TResponse>(
            this IServiceCollection services,
            Action<PollingServiceConfiguration>? configure = null)
        {
            // ── Shared singletons (idempotent) ────────────────────────────────
            services.TryAddSingleton<PollingHandlerRegistry>();
            services.TryAddSingleton<IPollingMetricsTracker, NoOpMetricsTracker>();
            services.TryAddSingleton<IPollingOrchestrator, PollingOrchestrator>();

            // Configuration — only apply once; callers using multiple type pairs
            // should pass the same config (or configure the options system separately).
            services.TryAddSingleton<IOptions<PollingServiceConfiguration>>(sp =>
            {
                var config = new PollingServiceConfiguration();
                configure?.Invoke(config);
                return Options.Create(config);
            });

            // ── Per type-pair singletons ──────────────────────────────────────
            services.TryAddSingleton(
                typeof(IPollingRepository<TRequest, TResponse>),
                typeof(ConcurrentPollingRepository<TRequest, TResponse>));

            services.TryAddSingleton<PollingService<TRequest, TResponse>>();

            services.AddHostedService(sp =>
                sp.GetRequiredService<PollingService<TRequest, TResponse>>());

            return services;
        }
    }
}
