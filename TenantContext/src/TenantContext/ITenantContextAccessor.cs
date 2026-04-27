namespace TenantContext;

/// <summary>
/// Read-only access to the ambient tenant for the current logical flow (async-local by default).
/// Consumers (caches, outbox, sagas, repositories, loggers) take a dependency on this rather than
/// resolving tenants themselves, keeping resolution policy in one place.
/// </summary>
public interface ITenantContextAccessor
{
    /// <summary>
    /// The current tenant for this logical flow, or <see cref="TenantInfo.None"/> when no tenant
    /// has been resolved. Never returns <c>null</c>.
    /// </summary>
    TenantInfo Current { get; }
}
