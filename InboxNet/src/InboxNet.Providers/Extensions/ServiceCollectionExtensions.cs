using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using InboxNet.Extensions;
using InboxNet.Interfaces;
using InboxNet.Options;
using InboxNet.Providers.Generic;
using InboxNet.Providers.GitHub;
using InboxNet.Providers.Stripe;

namespace InboxNet.Providers.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="StripeWebhookProvider"/> under the default key <c>"stripe"</c>.
    /// </summary>
    public static IInboxNetBuilder AddStripeProvider(
        this IInboxNetBuilder builder,
        Action<StripeWebhookOptions> configure)
    {
        builder.Services.Configure(configure);
        builder.Services.AddSingleton<IWebhookProvider, StripeWebhookProvider>();
        return builder;
    }

    /// <summary>
    /// Registers <see cref="GitHubWebhookProvider"/> under the default key <c>"github"</c>.
    /// </summary>
    public static IInboxNetBuilder AddGitHubProvider(
        this IInboxNetBuilder builder,
        Action<GitHubWebhookOptions> configure)
    {
        builder.Services.Configure(configure);
        builder.Services.AddSingleton<IWebhookProvider, GitHubWebhookProvider>();
        return builder;
    }

    /// <summary>
    /// Registers a <see cref="GenericHmacWebhookProvider"/> instance under the key set in
    /// <paramref name="configure"/>. Call multiple times with distinct keys to support
    /// several upstream services.
    /// </summary>
    public static IInboxNetBuilder AddGenericHmacProvider(
        this IInboxNetBuilder builder,
        Action<GenericHmacWebhookOptions> configure)
    {
        var opts = new GenericHmacWebhookOptions();
        configure(opts);

        if (string.IsNullOrWhiteSpace(opts.Key))
            throw new InvalidOperationException("GenericHmacWebhookOptions.Key must be set.");

        // Factory registration so multiple keyed providers can coexist; each closes over its
        // own captured options snapshot. The provider also pulls the global InboxOptions
        // from DI to honour AlwaysComputeContentSha256.
        builder.Services.AddSingleton<IWebhookProvider>(sp =>
            new GenericHmacWebhookProvider(opts, sp.GetRequiredService<IOptions<InboxOptions>>()));
        return builder;
    }
}
