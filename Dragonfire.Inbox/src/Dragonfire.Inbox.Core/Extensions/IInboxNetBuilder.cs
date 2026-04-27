using Microsoft.Extensions.DependencyInjection;

namespace Dragonfire.Inbox.Extensions;

public interface IInboxNetBuilder
{
    IServiceCollection Services { get; }
}
