using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using InboxNet.Dispatch;
using InboxNet.Interfaces;
using InboxNet.Options;
using InboxNet.Providers;
using InboxNet.Retry;
using InboxNet.Signals;

namespace InboxNet.Extensions;

public static class ServiceCollectionExtensions
{
    public static IInboxNetBuilder AddInboxNet(
        this IServiceCollection services,
        Action<InboxOptions>? configure = null)
    {
        // Use the options pipeline directly so multiple Configure calls and
        // IConfiguration.Bind callers compose normally.
        var optionsBuilder = services.AddOptions<InboxOptions>();
        if (configure is not null) optionsBuilder.Configure(configure);

        services.AddOptions<InboxRetryPolicyOptions>();

        services.AddSingleton<IInboxSignal, ChannelInboxSignal>();
        // Singleton: the registry is built once from the singleton provider list.
        // Composite providers that need scoped collaborators resolve them per-request
        // from the IServiceScopeFactory passed into ParseAsync.
        services.AddSingleton<IWebhookProviderRegistry, WebhookProviderRegistry>();
        services.AddSingleton<IInboxHandlerRegistry, InboxHandlerRegistry>();
        services.TryAddSingleton<IInboxRetryPolicy, ExponentialBackoffInboxRetryPolicy>();

        return new InboxNetBuilder(services);
    }

    /// <summary>
    /// Configures the retry policy applied to failed inbox messages. Replaces the default
    /// <see cref="ExponentialBackoffInboxRetryPolicy"/>'s options.
    /// </summary>
    public static IInboxNetBuilder ConfigureRetry(
        this IInboxNetBuilder builder,
        Action<InboxRetryPolicyOptions> configure)
    {
        builder.Services.Configure<InboxRetryPolicyOptions>(configure);
        return builder;
    }

    /// <summary>
    /// Registers <typeparamref name="TProvider"/> as a webhook provider. Providers validate
    /// and parse incoming requests before they are persisted; the <c>Key</c> property on the
    /// provider determines how it is resolved by the receive endpoint.
    /// </summary>
    public static IInboxNetBuilder AddProvider<TProvider>(this IInboxNetBuilder builder)
        where TProvider : class, IWebhookProvider
    {
        builder.Services.AddSingleton<IWebhookProvider, TProvider>();
        return builder;
    }

    /// <summary>
    /// Registers a provider instance.
    /// </summary>
    public static IInboxNetBuilder AddProvider(this IInboxNetBuilder builder, IWebhookProvider provider)
    {
        builder.Services.AddSingleton(provider);
        return builder;
    }

    /// <summary>
    /// Registers a provider as a keyed (<typeparamref name="TValidator"/>, <typeparamref name="TMapper"/>)
    /// pair under <paramref name="providerKey"/> and exposes them as an
    /// <see cref="IWebhookProvider"/> for the endpoint and registry. Use this when the
    /// signature check and payload parsing should be testable and replaceable independently.
    /// <para>
    /// Example:
    /// <code>
    /// builder.AddProvider&lt;AcmeSignatureValidator, AcmePayloadMapper&gt;("acme");
    /// // equivalent to the exchange-webhook decomposition with separate validator/mapper services.
    /// </code>
    /// </para>
    /// </summary>
    public static IInboxNetBuilder AddProvider<TValidator, TMapper>(
        this IInboxNetBuilder builder,
        string providerKey)
        where TValidator : class, IWebhookSignatureValidator
        where TMapper : class, IWebhookPayloadMapper
    {
        builder.Services.AddKeyedScoped<IWebhookSignatureValidator, TValidator>(providerKey);
        builder.Services.AddKeyedScoped<IWebhookPayloadMapper, TMapper>(providerKey);

        // Singleton composite that resolves its scoped collaborators per request from
        // an IServiceScopeFactory. Keeps the singleton registry usable while still letting
        // validators/mappers inject scoped services (DbContext etc.).
        builder.Services.AddSingleton<IWebhookProvider>(sp =>
            new CompositeWebhookProvider(
                providerKey,
                sp.GetRequiredService<IServiceScopeFactory>()));

        return builder;
    }

    /// <summary>
    /// Registers an inbox handler. Handlers run in registration order per message;
    /// each handler's success or failure is recorded separately in <c>InboxHandlerAttempts</c>,
    /// so a retry skips handlers that already succeeded.
    /// </summary>
    /// <param name="builder">The inbox builder.</param>
    /// <param name="configure">Optional callback to set <c>ProviderKey</c>, <c>EventType</c>, <c>MaxRetries</c>.</param>
    public static IInboxNetBuilder AddHandler<THandler>(
        this IInboxNetBuilder builder,
        Action<InboxHandlerRegistrationBuilder>? configure = null)
        where THandler : class, IInboxHandler
    {
        var regBuilder = new InboxHandlerRegistrationBuilder(typeof(THandler));
        configure?.Invoke(regBuilder);

        builder.Services.AddScoped<THandler>();

        var order = NextHandlerOrder(builder.Services);
        builder.Services.AddSingleton(regBuilder.Build(order));

        return builder;
    }

    private static int NextHandlerOrder(IServiceCollection services)
    {
        var existing = 0;
        foreach (var d in services)
        {
            if (d.ServiceType == typeof(InboxHandlerRegistration))
                existing++;
        }
        return existing;
    }
}

public sealed class InboxHandlerRegistrationBuilder
{
    private readonly Type _handlerType;
    private string? _providerKey;
    private string? _eventType;
    private int _maxRetries = 5;
    private string? _handlerName;

    internal InboxHandlerRegistrationBuilder(Type handlerType)
    {
        _handlerType = handlerType;
    }

    public InboxHandlerRegistrationBuilder ForProvider(string providerKey)
    {
        _providerKey = providerKey;
        return this;
    }

    public InboxHandlerRegistrationBuilder ForEvent(string eventType)
    {
        _eventType = eventType;
        return this;
    }

    public InboxHandlerRegistrationBuilder WithMaxRetries(int maxRetries)
    {
        _maxRetries = maxRetries;
        return this;
    }

    /// <summary>
    /// Override the persisted handler name. Defaults to the handler type's
    /// <see cref="Type.FullName"/>. Changing this value after deployment makes prior
    /// success records invisible — treat it as a stable identity.
    /// </summary>
    public InboxHandlerRegistrationBuilder WithName(string name)
    {
        _handlerName = name;
        return this;
    }

    internal InboxHandlerRegistration Build(int order) => new()
    {
        HandlerType = _handlerType,
        HandlerName = _handlerName ?? _handlerType.FullName ?? _handlerType.Name,
        ProviderKey = _providerKey,
        EventType = _eventType,
        MaxRetries = _maxRetries,
        Order = order
    };
}
