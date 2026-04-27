using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Dragonfire.Outbox.Context;
using Dragonfire.Outbox.Interfaces;
using Dragonfire.Outbox.Options;
using Dragonfire.Outbox.Secrets;
using Dragonfire.Outbox.Serialization;
using Dragonfire.Outbox.Signals;
using Dragonfire.Outbox.Subscriptions;

namespace Dragonfire.Outbox.Extensions;

public static class ServiceCollectionExtensions
{
    public static IOutboxNetBuilder AddOutboxNet(
        this IServiceCollection services,
        Action<OutboxOptions>? configure = null)
    {
        var options = new OutboxOptions();
        configure?.Invoke(options);

        services.Configure<OutboxOptions>(o =>
        {
            o.SchemaName = options.SchemaName;
            o.BatchSize = options.BatchSize;
            o.DefaultVisibilityTimeout = options.DefaultVisibilityTimeout;
            o.InstanceId = options.InstanceId;
            o.MaxConcurrentDeliveries = options.MaxConcurrentDeliveries;
            o.ProcessingMode = options.ProcessingMode;
            o.EnableOrderedProcessing = options.EnableOrderedProcessing;
            o.TenantFilter = options.TenantFilter;
        });

        services.AddSingleton<IMessageSerializer, JsonMessageSerializer>();
        // Default accessor returns null for both fields; override with UseHttpContextAccessor().
        services.AddScoped<IOutboxContextAccessor, NullOutboxContextAccessor>();
        // Default no-op reader; replaced when UseSqlServerContext / UseDirectSqlServer /
        // UseConfigWebhooks is called. Prevents startup crash if no store is registered.
        services.AddSingleton<ISubscriptionReader, NullSubscriptionReader>();
        // Push signal: singleton channel that lets publishers wake the processor immediately,
        // eliminating polling-interval latency for the first message after an idle period.
        services.AddSingleton<IOutboxSignal, ChannelOutboxSignal>();

        return new OutboxNetBuilder(services);
    }

    /// <summary>
    /// Replaces the default null context accessor with one that resolves TenantId and UserId
    /// from the current HTTP request's claims principal.
    /// </summary>
    public static IOutboxNetBuilder UseHttpContextAccessor(
        this IOutboxNetBuilder builder,
        Action<OutboxContextOptions>? configure = null)
    {
        var opts = new OutboxContextOptions();
        configure?.Invoke(opts);

        builder.Services.Configure<OutboxContextOptions>(o =>
        {
            o.TenantIdClaimType = opts.TenantIdClaimType;
            o.UserIdClaimType = opts.UserIdClaimType;
        });

        builder.Services.AddHttpContextAccessor();
        // Replaces the NullOutboxContextAccessor registered in AddOutboxNet.
        builder.Services.AddScoped<IOutboxContextAccessor, HttpContextOutboxContextAccessor>();

        return builder;
    }

    /// <summary>
    /// Registers a config-driven <see cref="ISubscriptionReader"/> that routes messages to
    /// webhook endpoints defined in <c>appsettings.json</c> under <c>Outbox:Webhooks</c>.
    /// Replaces any previously registered <see cref="ISubscriptionReader"/> / <see cref="ISubscriptionStore"/>
    /// (e.g. the EF Core one).
    /// </summary>
    /// <param name="builder">The outbox builder.</param>
    /// <param name="configuration">The application's <see cref="IConfiguration"/> root.</param>
    /// <param name="sectionName">Config section path; defaults to <c>"Outbox:Webhooks"</c>.</param>
    public static IOutboxNetBuilder UseConfigWebhooks(
        this IOutboxNetBuilder builder,
        IConfiguration configuration,
        string sectionName = WebhookOptions.SectionName)
    {
        builder.Services.Configure<WebhookOptions>(configuration.GetSection(sectionName));
        ReplaceSubscriptionReader<ConfigSubscriptionStore>(builder.Services);
        return builder;
    }

    /// <summary>
    /// Registers a config-driven <see cref="ISubscriptionReader"/> configured via a callback.
    /// Replaces any previously registered <see cref="ISubscriptionReader"/> / <see cref="ISubscriptionStore"/>.
    /// </summary>
    public static IOutboxNetBuilder UseConfigWebhooks(
        this IOutboxNetBuilder builder,
        Action<WebhookOptions> configure)
    {
        var opts = new WebhookOptions();
        configure(opts);

        builder.Services.Configure<WebhookOptions>(o =>
        {
            o.Mode = opts.Mode;
            o.Global = opts.Global;
            o.Tenants = opts.Tenants;
        });

        ReplaceSubscriptionReader<ConfigSubscriptionStore>(builder.Services);
        return builder;
    }

    /// <summary>
    /// Registers <see cref="ConfigurationTenantSecretRetriever"/> as <see cref="ITenantSecretRetriever"/>.
    /// Secrets are read from <see cref="IConfiguration"/> using a key pattern such as
    /// <c>"Outbox:Secrets:{tenantId}:WebhookSecret"</c>. When Azure Key Vault (or any other
    /// <see cref="IConfiguration"/> provider) is configured, secrets are transparently resolved
    /// from that store at delivery time without storing them in the database.
    /// </summary>
    /// <param name="builder">The outbox builder.</param>
    /// <param name="configure">Optional callback to customise <see cref="TenantSecretOptions"/>.</param>
    public static IOutboxNetBuilder UseTenantSecretRetriever(
        this IOutboxNetBuilder builder,
        Action<TenantSecretOptions>? configure = null)
    {
        var opts = new TenantSecretOptions();
        configure?.Invoke(opts);

        builder.Services.Configure<TenantSecretOptions>(o =>
        {
            o.KeyPattern = opts.KeyPattern;
            o.SecretCacheTtl = opts.SecretCacheTtl;
        });
        builder.Services.AddMemoryCache();
        builder.Services.AddScoped<ITenantSecretRetriever, ConfigurationTenantSecretRetriever>();

        return builder;
    }

    /// <summary>
    /// Registers a custom <typeparamref name="TRetriever"/> as <see cref="ITenantSecretRetriever"/>.
    /// </summary>
    public static IOutboxNetBuilder UseTenantSecretRetriever<TRetriever>(
        this IOutboxNetBuilder builder)
        where TRetriever : class, ITenantSecretRetriever
    {
        builder.Services.AddScoped<ITenantSecretRetriever, TRetriever>();
        return builder;
    }

    // Removes any existing ISubscriptionReader / ISubscriptionStore registrations and adds
    // the config-based reader so call order between UseConfigWebhooks and UseSqlServerContext
    // does not matter.
    // ConfigSubscriptionStore is registered as singleton: it reads from IOptions<WebhookOptions>
    // (itself a singleton) and has no per-request state.
    private static void ReplaceSubscriptionReader<TStore>(IServiceCollection services)
        where TStore : class, ISubscriptionReader
    {
        var existingStore = services.FirstOrDefault(d => d.ServiceType == typeof(ISubscriptionStore));
        if (existingStore is not null) services.Remove(existingStore);

        var existingReader = services.FirstOrDefault(d => d.ServiceType == typeof(ISubscriptionReader));
        if (existingReader is not null) services.Remove(existingReader);

        services.AddSingleton<ISubscriptionReader, TStore>();
    }
}
