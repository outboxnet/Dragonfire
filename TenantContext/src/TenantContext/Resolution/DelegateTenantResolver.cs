namespace TenantContext.Resolution;

/// <summary>
/// Adapts a delegate into an <see cref="ITenantResolver"/>. Convenient for one-off / inline rules
/// without creating a class (e.g. configuration-driven tenant lookup, custom claim mapping).
/// </summary>
public sealed class DelegateTenantResolver : ITenantResolver
{
    private readonly Func<TenantResolutionContext, CancellationToken, ValueTask<TenantResolution>> _resolve;

    public DelegateTenantResolver(string name, Func<TenantResolutionContext, CancellationToken, ValueTask<TenantResolution>> resolve)
    {
        Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Name required", nameof(name)) : name;
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
    }

    public string Name { get; }

    public ValueTask<TenantResolution> ResolveAsync(TenantResolutionContext context, CancellationToken cancellationToken)
        => _resolve(context, cancellationToken);
}
