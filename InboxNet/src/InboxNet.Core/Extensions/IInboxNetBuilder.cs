using Microsoft.Extensions.DependencyInjection;

namespace InboxNet.Extensions;

public interface IInboxNetBuilder
{
    IServiceCollection Services { get; }
}
