using Microsoft.EntityFrameworkCore;
using Dragonfire.Outbox.Interfaces;
using Dragonfire.Outbox.Models;

namespace Dragonfire.Outbox.EntityFrameworkCore.Stores;

internal sealed class EfCoreSubscriptionStore : ISubscriptionStore
{
    private readonly OutboxDbContext _dbContext;
    private readonly ITenantSecretRetriever? _secretRetriever;

    public EfCoreSubscriptionStore(
        OutboxDbContext dbContext,
        ITenantSecretRetriever? secretRetriever = null)
    {
        _dbContext = dbContext;
        _secretRetriever = secretRetriever;
    }

    public async Task<WebhookSubscription> AddAsync(WebhookSubscription subscription, CancellationToken ct = default)
    {
        subscription.CreatedAt = DateTimeOffset.UtcNow;
        _dbContext.WebhookSubscriptions.Add(subscription);
        await _dbContext.SaveChangesAsync(ct);
        return subscription;
    }

    public async Task<WebhookSubscription?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbContext.WebhookSubscriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<IReadOnlyList<WebhookSubscription>> GetByEventTypeAsync(string eventType, CancellationToken ct = default)
    {
        return await _dbContext.WebhookSubscriptions
            .Where(s => s.IsActive && (s.EventType == eventType || s.EventType == "*"))
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<WebhookSubscription>> GetForMessageAsync(OutboxMessage message, CancellationToken ct = default)
    {
        var tenantId = message.TenantId;

        // Global subscriptions (TenantId IS NULL) always match.
        // Tenant-specific subscriptions match only when TenantId equals the message's TenantId.
        // AsNoTracking: EnrichSecretsAsync mutates Secret on these objects; we must not let
        // EF Core track those mutations or a subsequent SaveChangesAsync will overwrite the DB.
        var subscriptions = await _dbContext.WebhookSubscriptions
            .Where(s => s.IsActive
                     && (s.EventType == message.EventType || s.EventType == "*")
                     && (s.TenantId == null || s.TenantId == tenantId))
            .AsNoTracking()
            .ToListAsync(ct);

        return await EnrichSecretsAsync(subscriptions, ct);
    }

    public async Task UpdateAsync(WebhookSubscription subscription, CancellationToken ct = default)
    {
        subscription.UpdatedAt = DateTimeOffset.UtcNow;
        _dbContext.WebhookSubscriptions.Update(subscription);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken ct = default)
    {
        await _dbContext.WebhookSubscriptions
            .Where(s => s.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.IsActive, false)
                .SetProperty(x => x.UpdatedAt, DateTimeOffset.UtcNow), ct);
    }

    private async Task<IReadOnlyList<WebhookSubscription>> EnrichSecretsAsync(
        List<WebhookSubscription> subscriptions,
        CancellationToken ct)
    {
        if (_secretRetriever is null || subscriptions.Count == 0)
            return subscriptions;

        // Fetch all secrets in parallel — important when the retriever calls Key Vault.
        await Task.WhenAll(subscriptions.Select(async sub =>
        {
            var tenantKey = sub.TenantId ?? sub.Id.ToString();
            var secret = await _secretRetriever.GetSecretAsync(tenantKey, ct);
            if (secret is not null)
                sub.Secret = secret;
        }));

        return subscriptions;
    }
}
