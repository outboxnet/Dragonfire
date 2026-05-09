using Dragonfire.IdempotentApi.Builder;
using Dragonfire.IdempotentApi.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Dragonfire.IdempotentApi.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Register the idempotent-API options and return a builder so transport / storage
    /// extensions can be attached fluently (e.g. <c>.AddAspNetCore().UseInMemoryStore()</c>).
    /// </summary>
    public static IIdempotentApiBuilder AddIdempotentApi(
        this IServiceCollection services,
        Action<IdempotentApiOptions>? configure = null)
    {
        if (configure is not null)
            services.Configure(configure);
        else
            services.AddOptions<IdempotentApiOptions>();

        return new IdempotentApiBuilder(services);
    }
}
