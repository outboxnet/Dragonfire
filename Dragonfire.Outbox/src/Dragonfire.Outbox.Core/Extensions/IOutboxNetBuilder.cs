using Microsoft.Extensions.DependencyInjection;

namespace Dragonfire.Outbox.Extensions;

public interface IOutboxNetBuilder
{
    IServiceCollection Services { get; }
}
