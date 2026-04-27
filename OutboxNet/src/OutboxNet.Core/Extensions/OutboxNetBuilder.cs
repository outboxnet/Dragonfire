using Microsoft.Extensions.DependencyInjection;

namespace OutboxNet.Extensions;

internal class OutboxNetBuilder : IOutboxNetBuilder
{
    public IServiceCollection Services { get; }

    public OutboxNetBuilder(IServiceCollection services)
    {
        Services = services;
    }
}
