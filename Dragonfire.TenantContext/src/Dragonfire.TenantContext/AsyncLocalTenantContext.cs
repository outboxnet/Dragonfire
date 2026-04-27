namespace Dragonfire.TenantContext;

/// <summary>
/// Default <see cref="ITenantContextAccessor"/> + <see cref="ITenantContextSetter"/> implementation
/// backed by <see cref="AsyncLocal{T}"/>. Flows across <c>async</c>/<c>await</c>, <see cref="Task.Run(Action)"/>,
/// and <see cref="System.Threading.Channels.Channel"/> readers as long as the consumer doesn't
/// suppress execution context (see <see cref="ExecutionContext.SuppressFlow"/>).
/// </summary>
/// <remarks>
/// The class is registered as a singleton; concurrent reads are safe because each logical flow
/// has its own copy of the holder reference.
/// </remarks>
public sealed class AsyncLocalTenantContext : ITenantContextAccessor, ITenantContextSetter
{
    private static readonly AsyncLocal<Holder?> _current = new();

    /// <inheritdoc />
    public TenantInfo Current => _current.Value?.Tenant ?? TenantInfo.None;

    /// <inheritdoc />
    public IDisposable BeginScope(TenantInfo tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        var parent = _current.Value;
        _current.Value = new Holder(tenant);
        return new Scope(parent);
    }

    private sealed class Holder
    {
        public Holder(TenantInfo tenant) => Tenant = tenant;
        public TenantInfo Tenant { get; }
    }

    private sealed class Scope : IDisposable
    {
        private readonly Holder? _parent;
        private int _disposed;

        public Scope(Holder? parent) => _parent = parent;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _current.Value = _parent;
            }
        }
    }
}
