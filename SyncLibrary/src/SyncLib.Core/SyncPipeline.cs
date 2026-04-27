using Microsoft.Extensions.DependencyInjection;
using SyncLib.Abstractions;

namespace SyncLib.Core;

/// <summary>
/// The fetch → handle pipeline executed once per sync run for a single stream.
/// Resolves the user-supplied API client <typeparamref name="TClient"/> and an
/// <see cref="ISyncDataHandler{TDto}"/> from the supplied scope, asks the state
/// store for the last successful run (for incremental fetches), invokes the
/// configured fetch delegate, and hands the resulting DTOs to the consumer's
/// handler. Persistence is intentionally the consumer's responsibility — the
/// library never touches the application's database.
/// </summary>
/// <remarks>
/// Override <see cref="ExecuteAsync"/> in a derived class to customise the
/// pipeline (e.g. to filter, batch, or run multiple fetch passes). Register the
/// derived pipeline by calling
/// <see cref="SyncStreamRegistration.Create{TClient,TDto}(SyncPipeline{TClient,TDto}, Configuration.StreamConfiguration)"/>.
/// </remarks>
public class SyncPipeline<TClient, TDto>
    where TClient : class
    where TDto : class
{
    /// <summary>Delegate signature used to fetch DTOs from the API client.</summary>
    /// <param name="client">The API client resolved from DI.</param>
    /// <param name="lastSuccessAt">Start of the most recent successful run, or <c>null</c> if none.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public delegate Task<IReadOnlyCollection<TDto>> FetchDelegate(
        TClient client,
        DateTime? lastSuccessAt,
        CancellationToken cancellationToken);

    /// <summary>The composite identity of the stream this pipeline serves.</summary>
    public SyncStateKey Key { get; }

    /// <summary>The fetch delegate this pipeline uses to load DTOs from the client.</summary>
    public FetchDelegate Fetch { get; }

    /// <summary>Construct a pipeline bound to a stream key and a fetch delegate.</summary>
    public SyncPipeline(SyncStateKey key, FetchDelegate fetch)
    {
        ArgumentNullException.ThrowIfNull(fetch);
        if (string.IsNullOrWhiteSpace(key.ProviderName))
        {
            throw new ArgumentException("ProviderName must be provided.", nameof(key));
        }
        if (string.IsNullOrWhiteSpace(key.StreamName))
        {
            throw new ArgumentException("StreamName must be provided.", nameof(key));
        }
        Key = key;
        Fetch = fetch;
    }

    /// <summary>Run the pipeline once and return the number of DTOs handled.</summary>
    /// <param name="services">A scoped <see cref="IServiceProvider"/> created by the runner.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public virtual async Task<int> ExecuteAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);

        var stateStore = services.GetRequiredService<ISyncStateStore>();
        var client = services.GetRequiredService<TClient>();
        var handler = services.GetRequiredService<ISyncDataHandler<TDto>>();

        var lastRun = await stateStore.GetAsync(Key, cancellationToken).ConfigureAwait(false);
        var data = await Fetch(client, lastRun?.LastSuccessAt, cancellationToken).ConfigureAwait(false);

        var context = new SyncContext
        {
            Key = Key,
            DtoType = typeof(TDto),
            StartedAtUtc = DateTime.UtcNow,
            LastSuccessAt = lastRun?.LastSuccessAt
        };

        await handler.HandleAsync(context, data, cancellationToken).ConfigureAwait(false);
        return data.Count;
    }
}
