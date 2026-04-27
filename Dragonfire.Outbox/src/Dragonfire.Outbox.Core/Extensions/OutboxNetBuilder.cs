using Microsoft.Extensions.DependencyInjection;

namespace Dragonfire.Outbox.Extensions;

internal class OutboxNetBuilder : IOutboxNetBuilder
{
    public IServiceCollection Services { get; }

    public OutboxNetBuilder(IServiceCollection services)
    {
        Services = services;
    }
}
