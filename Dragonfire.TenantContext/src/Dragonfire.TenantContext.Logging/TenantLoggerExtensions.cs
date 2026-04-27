using Microsoft.Extensions.Logging;

namespace Dragonfire.TenantContext.Logging;

/// <summary>
/// Helpers to push the current tenant id into <see cref="ILogger.BeginScope{TState}"/>, so any
/// log written inside the returned scope carries a structured <c>TenantId</c> property.
/// Compatible with Serilog, NLog, MEL JSON formatter, OTel logs — anything that surfaces scopes.
/// </summary>
public static class TenantLoggerExtensions
{
    private const string TenantIdProperty = "TenantId";
    private const string TenantSourceProperty = "TenantSource";

    /// <summary>Begins a logger scope containing the current tenant id, or <c>null</c> if none.</summary>
    public static IDisposable? BeginTenantScope(this ILogger logger, ITenantContextAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(accessor);

        var tenant = accessor.Current;
        if (!tenant.IsResolved) return null;

        return logger.BeginScope(new Dictionary<string, object>
        {
            [TenantIdProperty] = tenant.TenantId.Value,
            [TenantSourceProperty] = tenant.Source,
        });
    }

    /// <summary>Begins a logger scope for the supplied tenant. Useful in workers that just dequeued a message.</summary>
    public static IDisposable? BeginTenantScope(this ILogger logger, TenantInfo tenant)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(tenant);

        if (!tenant.IsResolved) return null;
        return logger.BeginScope(new Dictionary<string, object>
        {
            [TenantIdProperty] = tenant.TenantId.Value,
            [TenantSourceProperty] = tenant.Source,
        });
    }
}
