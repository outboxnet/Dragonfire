namespace Dragonfire.Outbox.Options;

/// <summary>
/// Options for <see cref="Dragonfire.Outbox.Secrets.ConfigurationTenantSecretRetriever"/>.
/// </summary>
public class TenantSecretOptions
{
    /// <summary>
    /// <para>
    /// IConfiguration key pattern used to locate each tenant's webhook secret.
    /// Use <c>{tenantId}</c> as a placeholder — it is replaced with the actual tenant ID at runtime.
    /// </para>
    /// <para>
    /// Default: <c>"Outbox:Secrets:{tenantId}:WebhookSecret"</c>
    /// </para>
    /// <para>
    /// <b>Azure Key Vault note:</b> Key Vault flattens the hierarchy by replacing colons with
    /// double-dashes. The default pattern therefore maps to a vault secret named
    /// <c>Outbox--Secrets--{tenantId}--WebhookSecret</c> (e.g. <c>Outbox--Secrets--tenant-a--WebhookSecret</c>).
    /// </para>
    /// </summary>
    public string KeyPattern { get; set; } = "Outbox:Secrets:{tenantId}:WebhookSecret";

    /// <summary>
    /// How long a resolved secret is cached in memory before being re-read from IConfiguration
    /// (and thus from Key Vault, environment variables, etc.).
    /// Set to <see cref="TimeSpan.Zero"/> to disable caching.
    /// Default: 5 minutes.
    /// </summary>
    public TimeSpan SecretCacheTtl { get; set; } = TimeSpan.FromMinutes(5);
}
