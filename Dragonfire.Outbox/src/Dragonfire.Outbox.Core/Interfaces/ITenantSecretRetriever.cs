namespace Dragonfire.Outbox.Interfaces;

/// <summary>
/// Retrieves the HMAC signing secret for a tenant's webhook subscription at delivery time.
/// Implement this interface to pull secrets from Azure Key Vault (via IConfiguration),
/// a secrets manager, or any other store, instead of keeping them in the database.
/// </summary>
public interface ITenantSecretRetriever
{
    /// <summary>
    /// Returns the secret for <paramref name="tenantId"/>, or <c>null</c> to fall back
    /// to the secret already stored on the <see cref="Dragonfire.Outbox.Models.WebhookSubscription"/> record.
    /// </summary>
    Task<string?> GetSecretAsync(string tenantId, CancellationToken ct = default);
}
