using Dragonfire.IdempotentApi.Builder;
using Dragonfire.IdempotentApi.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dragonfire.IdempotentApi.InMemory.Extensions;

public static class IdempotentApiBuilderExtensions
{
    /// <summary>
    /// Register the in-memory store. Use only for testing, development, or a single-instance
    /// deployment — entries are lost on process restart and never replicated across hosts.
    /// </summary>
    public static IIdempotentApiBuilder UseInMemoryStore(this IIdempotentApiBuilder builder)
    {
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
        return builder;
    }
}
