using Microsoft.Extensions.DependencyInjection;

namespace Dragonfire.TraceKit.Extensions;

/// <summary>
/// Fluent builder returned by <see cref="ServiceCollectionExtensions.AddTraceKit"/>.
/// Callers chain calls like <c>.UseRepository&lt;MyRepo&gt;()</c> off this surface.
/// </summary>
public interface ITraceKitBuilder
{
    /// <summary>The underlying service collection.</summary>
    IServiceCollection Services { get; }
}
