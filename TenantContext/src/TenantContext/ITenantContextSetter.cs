namespace TenantContext;

/// <summary>
/// Mutates the ambient tenant for a bounded scope. Disposing the returned scope restores
/// the previous tenant, enabling safe nesting (e.g. tenant-switching inside a request).
/// </summary>
public interface ITenantContextSetter
{
    /// <summary>
    /// Sets <paramref name="tenant"/> as the current tenant until the returned <see cref="IDisposable"/>
    /// is disposed, at which point the previous tenant is restored.
    /// </summary>
    IDisposable BeginScope(TenantInfo tenant);
}
