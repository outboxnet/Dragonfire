using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Dragonfire.Outbox.Interfaces;
using Dragonfire.Outbox.Options;

namespace Dragonfire.Outbox.Secrets;

/// <summary>
/// Reads webhook secrets from <see cref="IConfiguration"/> using a configurable key pattern.
/// Because <see cref="IConfiguration"/> is provider-agnostic, this implementation transparently
/// supports Azure Key Vault, AWS Secrets Manager, environment variables, or any other provider
/// that has been added to the application's configuration pipeline.
/// </summary>
/// <remarks>
/// Secrets are cached in memory for <see cref="TenantSecretOptions.SecretCacheTtl"/> (default 5 min)
/// to avoid a Key Vault round-trip on every delivery. Set <c>SecretCacheTtl = TimeSpan.Zero</c> to
/// disable caching.
/// </remarks>
public sealed class ConfigurationTenantSecretRetriever : ITenantSecretRetriever
{
    private readonly IConfiguration _configuration;
    private readonly TenantSecretOptions _options;
    private readonly IMemoryCache _cache;

    public ConfigurationTenantSecretRetriever(
        IConfiguration configuration,
        IOptions<TenantSecretOptions> options,
        IMemoryCache cache)
    {
        _configuration = configuration;
        _options = options.Value;
        _cache = cache;
    }

    /// <inheritdoc />
    public Task<string?> GetSecretAsync(string tenantId, CancellationToken ct = default)
    {
        if (_options.SecretCacheTtl == TimeSpan.Zero)
            return Task.FromResult(Resolve(tenantId));

        var cacheKey = $"outbox:secret:{tenantId}";

        // GetOrCreate is atomic under IMemoryCache — only one factory invocation runs
        // per key even under concurrent access, preventing multiple Key Vault round-trips.
        var secret = _cache.GetOrCreate(cacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _options.SecretCacheTtl;
            return Resolve(tenantId);
        });

        return Task.FromResult(secret);
    }

    private string? Resolve(string tenantId)
    {
        var key = _options.KeyPattern.Replace("{tenantId}", tenantId,
            StringComparison.OrdinalIgnoreCase);
        return _configuration[key];
    }
}
