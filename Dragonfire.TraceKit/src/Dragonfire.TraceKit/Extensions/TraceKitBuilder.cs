using Microsoft.Extensions.DependencyInjection;

namespace Dragonfire.TraceKit.Extensions;

internal sealed class TraceKitBuilder : ITraceKitBuilder
{
    public TraceKitBuilder(IServiceCollection services) => Services = services;
    public IServiceCollection Services { get; }
}
