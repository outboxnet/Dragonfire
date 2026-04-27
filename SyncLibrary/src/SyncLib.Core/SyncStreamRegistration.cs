using SyncLib.Abstractions;
using SyncLib.Core.Configuration;

namespace SyncLib.Core;

/// <summary>
/// DI-resolved registration capturing the closed generic types and
/// configuration of one sync stream. Created by the <c>AddSyncStream</c>
/// fluent builder; consumed by <see cref="SyncRunner"/>. Consumers do not
/// normally instantiate this type directly.
/// </summary>
public sealed class SyncStreamRegistration
{
    /// <summary>Composite identity of the stream (provider + stream name).</summary>
    public required SyncStateKey Key { get; init; }

    /// <summary>The API client interface registered for this stream.</summary>
    public required Type ClientType { get; init; }

    /// <summary>The DTO type produced by the fetch delegate.</summary>
    public required Type DtoType { get; init; }

    /// <summary>Per-stream schedule, retry and circuit-breaker settings.</summary>
    public required StreamConfiguration Configuration { get; init; }

    /// <summary>
    /// Executes one run of the pipeline against the supplied scope and returns
    /// the number of DTOs handed to the handler. The runner adds retry,
    /// breaker, state recording and metrics around this call.
    /// </summary>
    public required Func<IServiceProvider, CancellationToken, Task<int>> ExecuteAsync { get; init; }

    /// <summary>Build a registration that uses the default <see cref="SyncPipeline{TClient, TDto}"/>.</summary>
    public static SyncStreamRegistration Create<TClient, TDto>(
        SyncStateKey key,
        SyncPipeline<TClient, TDto>.FetchDelegate fetch,
        StreamConfiguration configuration)
        where TClient : class
        where TDto : class
        => Create(new SyncPipeline<TClient, TDto>(key, fetch), configuration);

    /// <summary>Build a registration around a custom (typically derived) pipeline.</summary>
    public static SyncStreamRegistration Create<TClient, TDto>(
        SyncPipeline<TClient, TDto> pipeline,
        StreamConfiguration configuration)
        where TClient : class
        where TDto : class
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(configuration);
        return new SyncStreamRegistration
        {
            Key = pipeline.Key,
            ClientType = typeof(TClient),
            DtoType = typeof(TDto),
            Configuration = configuration,
            ExecuteAsync = pipeline.ExecuteAsync
        };
    }
}
