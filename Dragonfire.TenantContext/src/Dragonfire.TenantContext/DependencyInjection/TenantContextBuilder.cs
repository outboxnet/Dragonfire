using Microsoft.Extensions.DependencyInjection;

namespace Dragonfire.TenantContext.DependencyInjection;

/// <summary>
/// Fluent builder returned by <c>AddTenantContext</c>. Adapter packages (ASP.NET Core, gRPC, Http,
/// Logging, Tasks) hang their own extension methods off this type so registration stays composable
/// and discoverable.
/// </summary>
public sealed class TenantContextBuilder
{
    public TenantContextBuilder(IServiceCollection services) => Services = services ?? throw new ArgumentNullException(nameof(services));

    public IServiceCollection Services { get; }
}
