using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Dragonfire.Outbox.Interfaces;
using Dragonfire.Outbox.Models;
using Dragonfire.Outbox.Options;

namespace Dragonfire.Outbox.Subscriptions;

/// <summary>
/// An <see cref="ISubscriptionStore"/> that is driven entirely by <see cref="WebhookOptions"/>
/// loaded from configuration (e.g. appsettings.json). No database table is used.
///
/// <para>Supports two routing modes:</para>
/// <list type="bullet">
///   <item><term>Global</term><description>All messages go to the single configured endpoint.</description></item>
///   <item><term>PerTenant</term><description>Messages are routed to the endpoint registered for their TenantId.
///   An optional <c>"default"</c> entry acts as a catch-all for messages whose TenantId is null or unknown.</description></item>
/// </list>
/// </summary>
public sealed class ConfigSubscriptionStore : ISubscriptionReader
{
    private readonly WebhookOptions _options;
    private readonly ILogger<ConfigSubscriptionStore> _logger;

    public ConfigSubscriptionStore(
        IOptions<WebhookOptions> options,
        ILogger<ConfigSubscriptionStore> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<IReadOnlyList<WebhookSubscription>> GetForMessageAsync(
        OutboxMessage message,
        CancellationToken ct = default)
    {
        var subscriptions = _options.Mode switch
        {
            WebhookMode.Global    => ResolveGlobal(),
            WebhookMode.PerTenant => ResolveForTenant(message.TenantId),
            _                     => []
        };

        return Task.FromResult<IReadOnlyList<WebhookSubscription>>(subscriptions);
    }

    public Task<IReadOnlyList<WebhookSubscription>> GetByEventTypeAsync(
        string eventType,
        CancellationToken ct = default)
        // When used as a fallback (e.g. during unit tests), route globally ignoring event type.
        => GetForMessageAsync(new OutboxMessage { EventType = eventType }, ct);

    public Task<WebhookSubscription?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult<WebhookSubscription?>(null);

    // ── Private helpers ────────────────────────────────────────────────────────

    private IReadOnlyList<WebhookSubscription> ResolveGlobal()
    {
        if (_options.Global is null)
        {
            _logger.LogWarning(
                "WebhookMode is Global but no Global endpoint is configured under '{Section}'.",
                WebhookOptions.SectionName);
            return [];
        }

        return [ToSubscription(_options.Global, tenantId: null)];
    }

    private IReadOnlyList<WebhookSubscription> ResolveForTenant(string? tenantId)
    {
        if (tenantId is not null && _options.Tenants.TryGetValue(tenantId, out var tenantConfig))
            return [ToSubscription(tenantConfig, tenantId)];

        // Fall back to the "default" catch-all entry if present.
        if (_options.Tenants.TryGetValue("default", out var defaultConfig))
            return [ToSubscription(defaultConfig, tenantId)];

        _logger.LogWarning(
            "No webhook endpoint configured for TenantId '{TenantId}' and no 'default' entry found.",
            tenantId ?? "(null)");

        return [];
    }

    private static WebhookSubscription ToSubscription(WebhookEndpointConfig config, string? tenantId)
        => new()
        {
            // Deterministic ID so delivery-attempt records can reference it consistently.
            Id = DeterministicId(config.Url, tenantId),
            EventType = "*",
            WebhookUrl = config.Url,
            Secret = config.Secret,
            IsActive = true,
            MaxRetries = config.MaxRetries,
            Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds),
            CustomHeaders = config.CustomHeaders,
            CreatedAt = DateTimeOffset.UtcNow
        };

    /// <summary>
    /// Produces a stable <see cref="Guid"/> from the endpoint URL + tenant so that the same
    /// logical subscription always has the same ID across restarts (no DB row needed).
    /// </summary>
    private static Guid DeterministicId(string url, string? tenantId)
    {
        var key = $"{url}|{tenantId ?? string.Empty}";
        var hash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(key));
        // Force version 3 (name-based MD5) UUID layout.
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash);
    }
}
