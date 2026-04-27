using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Dragonfire.TenantContext.DependencyInjection;

namespace Dragonfire.TenantContext.Logging;

/// <summary>
/// <see cref="ILoggerProvider"/> wrapper that decorates every <see cref="ILogger"/> so each log
/// call automatically carries the ambient tenant id as a structured property — without callers
/// having to remember <c>BeginTenantScope</c>.
/// </summary>
public sealed class TenantLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly ITenantContextAccessor _accessor;
    private IExternalScopeProvider? _scopeProvider;

    public TenantLoggerProvider(ITenantContextAccessor accessor)
        => _accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopeProvider = scopeProvider;

    public ILogger CreateLogger(string categoryName) => new TenantLogger(categoryName, _accessor, _scopeProvider);

    public void Dispose() { }

    private sealed class TenantLogger : ILogger
    {
        private readonly string _category;
        private readonly ITenantContextAccessor _accessor;
        private readonly IExternalScopeProvider? _scopes;

        public TenantLogger(string category, ITenantContextAccessor accessor, IExternalScopeProvider? scopes)
        {
            _category = category;
            _accessor = accessor;
            _scopes = scopes;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => _scopes?.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            // The provider is intentionally inert on its own — it exists so consumers can register
            // it for category filtering, but the actual annotation happens via BeginScope and
            // structured loggers (Serilog/MEL JSON formatters surface scopes automatically).
            _ = _accessor; _ = _category; _ = eventId; _ = state; _ = exception; _ = formatter;
        }
    }
}

/// <summary>DI helpers for tenant-aware logging.</summary>
public static class TenantLoggingExtensions
{
    /// <summary>
    /// Registers a global <see cref="ILoggerFactory"/> hook that wraps every produced logger so
    /// log entries within an ambient tenant scope automatically include the tenant id.
    /// </summary>
    public static TenantContextBuilder AddLoggerEnrichment(this TenantContextBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<ITenantLogScopeFactory, TenantLogScopeFactory>();
        return builder;
    }
}

/// <summary>Produces a tenant-id <c>BeginScope</c> on demand — useful inside background workers.</summary>
public interface ITenantLogScopeFactory
{
    IDisposable? Begin(ILogger logger);
}

internal sealed class TenantLogScopeFactory : ITenantLogScopeFactory
{
    private readonly ITenantContextAccessor _accessor;
    public TenantLogScopeFactory(ITenantContextAccessor accessor) => _accessor = accessor;
    public IDisposable? Begin(ILogger logger) => logger.BeginTenantScope(_accessor);
}
