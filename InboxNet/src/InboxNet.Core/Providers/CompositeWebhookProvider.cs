using Microsoft.Extensions.DependencyInjection;
using InboxNet.Interfaces;
using InboxNet.Models;

namespace InboxNet.Providers;

/// <summary>
/// Bridges a keyed (<see cref="IWebhookSignatureValidator"/>, <see cref="IWebhookPayloadMapper"/>)
/// pair into the <see cref="IWebhookProvider"/> seam the registry and endpoint consume.
/// Resolved as a singleton; opens a fresh DI scope per <see cref="ParseAsync"/> call so the
/// keyed validator and mapper can inject scoped collaborators (DbContext, tenant resolvers, …).
/// Registered indirectly via <c>AddProvider&lt;TValidator, TMapper&gt;(providerKey)</c>.
/// </summary>
internal sealed class CompositeWebhookProvider : IWebhookProvider
{
    private readonly IServiceScopeFactory _scopeFactory;

    public CompositeWebhookProvider(string key, IServiceScopeFactory scopeFactory)
    {
        Key = key;
        _scopeFactory = scopeFactory;
    }

    public string Key { get; }

    public async Task<WebhookParseResult> ParseAsync(
        WebhookRequestContext context,
        CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;

        var validator = sp.GetRequiredKeyedService<IWebhookSignatureValidator>(Key);
        var mapper = sp.GetRequiredKeyedService<IWebhookPayloadMapper>(Key);

        var validation = await validator.ValidateAsync(context, ct);
        if (!validation.IsValid)
            return WebhookParseResult.Invalid(validation.FailureReason ?? "Signature validation failed");

        return await mapper.MapAsync(context, ct);
    }
}
