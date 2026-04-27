using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Dragonfire.Sync.Abstractions;

namespace Dragonfire.Sync.Core.DependencyInjection;

/// <summary>DI extensions for Dragonfire.Sync.</summary>
public static class SyncServiceCollectionExtensions
{
    /// <summary>
    /// Register the on-demand <see cref="ISyncRunner"/> and an in-memory state store.
    /// Does <b>not</b> register a <see cref="IHostedService"/>. Use this from
    /// hosts that drive scheduling externally (Azure Functions timer trigger,
    /// console job, controller endpoint, …).
    /// </summary>
    public static IServiceCollection AddSyncRunner(this IServiceCollection services)
    {
        services.TryAddSingleton<ISyncStateStore, InMemorySyncStateStore>();
        services.TryAddSingleton(sp => (ISyncStateReader)sp.GetRequiredService<ISyncStateStore>());
        services.TryAddSingleton<ISyncRunner, SyncRunner>();
        return services;
    }

    /// <summary>
    /// Register everything <see cref="AddSyncRunner"/> registers, plus the hosted
    /// <see cref="SyncOrchestrator"/> that schedules each stream on its
    /// configured interval. Use from long-lived hosts (ASP.NET, Worker Service).
    /// </summary>
    public static IServiceCollection AddSyncLibrary(this IServiceCollection services)
    {
        services.AddSyncRunner();

        services.AddSingleton<SyncOrchestrator>();
        services.AddSingleton<ISyncOrchestrator>(sp => sp.GetRequiredService<SyncOrchestrator>());
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<SyncOrchestrator>());

        return services;
    }

    /// <summary>
    /// Begin building a sync stream registration. The stream is identified by
    /// (<paramref name="providerName"/>, <paramref name="streamName"/>) so a single
    /// provider may register multiple streams (one per DTO type). Call
    /// <c>WithFetch</c>, <c>HandledBy</c>, optionally <c>WithSchedule</c>/<c>Configure</c>,
    /// then <c>Build()</c>.
    /// </summary>
    /// <typeparam name="TClient">The API client interface — registered by the consumer in DI.</typeparam>
    /// <typeparam name="TDto">The DTO type produced by the fetch delegate and consumed by the handler.</typeparam>
    public static ISyncStreamBuilder<TClient, TDto> AddSyncStream<TClient, TDto>(
        this IServiceCollection services,
        string providerName,
        string streamName)
        where TClient : class
        where TDto : class
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            throw new ArgumentException("Provider name must be provided.", nameof(providerName));
        }
        if (string.IsNullOrWhiteSpace(streamName))
        {
            throw new ArgumentException("Stream name must be provided.", nameof(streamName));
        }
        return new SyncStreamBuilder<TClient, TDto>(services, new SyncStateKey(providerName, streamName));
    }
}
