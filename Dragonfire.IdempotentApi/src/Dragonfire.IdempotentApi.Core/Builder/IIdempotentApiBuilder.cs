using Microsoft.Extensions.DependencyInjection;

namespace Dragonfire.IdempotentApi.Builder;

/// <summary>
/// Fluent builder returned by <c>AddIdempotentApi</c>. Extension assemblies (AspNetCore,
/// EF Core, InMemory, Redis, ...) attach themselves via extension methods on this type.
/// </summary>
public interface IIdempotentApiBuilder
{
    IServiceCollection Services { get; }
}
