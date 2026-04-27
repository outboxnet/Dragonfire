namespace Dragonfire.TenantContext.Tasks;

/// <summary>
/// Snapshot of the ambient tenant taken at enqueue time, ready to be re-applied on a worker
/// thread later. Use when work is dispatched through a queue/channel that doesn't preserve
/// <see cref="System.Threading.ExecutionContext"/>, or when the worker thread comes from a
/// long-lived pool that already has its own ambient state.
/// </summary>
public readonly struct TenantContextCapture
{
    private readonly TenantInfo _tenant;
    private readonly ITenantContextSetter _setter;

    public TenantContextCapture(ITenantContextAccessor accessor, ITenantContextSetter setter)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        _setter = setter ?? throw new ArgumentNullException(nameof(setter));
        _tenant = accessor.Current;
    }

    /// <summary>The captured tenant; <see cref="TenantInfo.None"/> when none was ambient at capture.</summary>
    public TenantInfo Tenant => _tenant;

    /// <summary>
    /// Restores the captured tenant for the duration of the returned scope. Returns a no-op scope
    /// when nothing was captured, so callers can use <c>using</c> unconditionally.
    /// </summary>
    public IDisposable Restore() => _tenant.IsResolved ? _setter.BeginScope(_tenant) : NoopScope.Instance;

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();
        public void Dispose() { }
    }
}
