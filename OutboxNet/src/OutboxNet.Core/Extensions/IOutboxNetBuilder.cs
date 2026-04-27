using Microsoft.Extensions.DependencyInjection;

namespace OutboxNet.Extensions;

public interface IOutboxNetBuilder
{
    IServiceCollection Services { get; }
}
