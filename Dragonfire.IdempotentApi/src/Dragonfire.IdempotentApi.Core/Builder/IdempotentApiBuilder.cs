using Microsoft.Extensions.DependencyInjection;

namespace Dragonfire.IdempotentApi.Builder;

internal sealed class IdempotentApiBuilder : IIdempotentApiBuilder
{
    public IdempotentApiBuilder(IServiceCollection services) => Services = services;
    public IServiceCollection Services { get; }
}
