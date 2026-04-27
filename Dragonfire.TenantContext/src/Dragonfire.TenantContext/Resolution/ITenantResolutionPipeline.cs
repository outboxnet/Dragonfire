namespace Dragonfire.TenantContext.Resolution;

/// <summary>
/// High-level entry point used by adapters (ASP.NET middleware, gRPC interceptor, message handlers)
/// to translate a raw transport context into a <see cref="TenantInfo"/>.
/// </summary>
public interface ITenantResolutionPipeline
{
    /// <summary>
    /// Runs all configured <see cref="ITenantResolver"/>s under <see cref="TenantResolutionOptions"/>
    /// and returns the resulting tenant info. Returns <see cref="TenantInfo.None"/> when no tenant
    /// could be resolved and policy permits an empty tenant.
    /// </summary>
    /// <exception cref="TenantResolutionException">When policy forbids the outcome.</exception>
    ValueTask<TenantInfo> ResolveAsync(TenantResolutionContext context, CancellationToken cancellationToken = default);
}
