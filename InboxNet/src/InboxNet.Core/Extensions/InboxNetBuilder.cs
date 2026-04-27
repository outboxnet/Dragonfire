using Microsoft.Extensions.DependencyInjection;

namespace InboxNet.Extensions;

internal class InboxNetBuilder : IInboxNetBuilder
{
    public IServiceCollection Services { get; }

    public InboxNetBuilder(IServiceCollection services)
    {
        Services = services;
    }
}
