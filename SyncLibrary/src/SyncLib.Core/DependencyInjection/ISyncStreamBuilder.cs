using Microsoft.Extensions.DependencyInjection;
using SyncLib.Abstractions;
using SyncLib.Core.Configuration;

namespace SyncLib.Core.DependencyInjection;

/// <summary>
/// Fluent builder for registering one sync stream. Use
/// <see cref="SyncServiceCollectionExtensions.AddSyncStream{TClient,TDto}"/> to obtain one.
/// </summary>
/// <remarks>
/// The API client of type <typeparamref name="TClient"/> is registered by the
/// consumer (e.g. <c>services.AddHttpClient&lt;IWeatherApi, OpenWeatherApi&gt;()</c>).
/// SyncLib only resolves it from the per-run scope at execution time.
/// </remarks>
public interface ISyncStreamBuilder<TClient, TDto>
    where TClient : class
    where TDto : class
{
    /// <summary>Configure the fetch delegate that pulls DTOs from the API client.</summary>
    ISyncStreamBuilder<TClient, TDto> WithFetch(SyncPipeline<TClient, TDto>.FetchDelegate fetch);

    /// <summary>Register a custom (typically derived) <see cref="SyncPipeline{TClient,TDto}"/> instead of the default one.</summary>
    ISyncStreamBuilder<TClient, TDto> WithPipeline(SyncPipeline<TClient, TDto> pipeline);

    /// <summary>Register the handler (scoped) that receives DTOs at the end of the pipeline.</summary>
    ISyncStreamBuilder<TClient, TDto> HandledBy<THandler>()
        where THandler : class, ISyncDataHandler<TDto>;

    /// <summary>Register a pre-built handler instance as a singleton.</summary>
    ISyncStreamBuilder<TClient, TDto> HandledBy(ISyncDataHandler<TDto> handler);

    /// <summary>Mutate the per-stream configuration in place.</summary>
    ISyncStreamBuilder<TClient, TDto> Configure(Action<StreamConfiguration> configure);

    /// <summary>Convenience: set the schedule on the stream's <see cref="StreamConfiguration"/>.</summary>
    ISyncStreamBuilder<TClient, TDto> WithSchedule(TimeSpan interval);

    /// <summary>Validate the registration and add it to the service collection.</summary>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    IServiceCollection Build();
}
