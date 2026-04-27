using Microsoft.Extensions.DependencyInjection;
using Dragonfire.Sync.Abstractions;
using Dragonfire.Sync.Core;
using Xunit;

namespace Dragonfire.Sync.Core.Tests;

public class SyncPipelineTests
{
    private static readonly SyncStateKey Key = new("p", "default");

    [Fact]
    public async Task Execute_FullFetch_WhenNoPriorSuccess()
    {
        var (sp, client, handler) = BuildScope(lastSuccess: null);

        var pipeline = new SyncPipeline<IFakeClient, Dto>(Key, (c, since, ct) => c.FetchAsync(since, ct));
        var count = await pipeline.ExecuteAsync(sp, default);

        Assert.Equal(2, count);
        Assert.Null(client.LastFetchSince);
        Assert.Single(handler.HandledBatches);
        Assert.Equal(2, handler.HandledBatches[0].Count);
    }

    [Fact]
    public async Task Execute_PassesLastSuccessAt_ToFetch()
    {
        var since = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var (sp, client, _) = BuildScope(lastSuccess: since);

        var pipeline = new SyncPipeline<IFakeClient, Dto>(Key, (c, last, ct) => c.FetchAsync(last, ct));
        await pipeline.ExecuteAsync(sp, default);

        Assert.Equal(since, client.LastFetchSince);
    }

    [Fact]
    public async Task Handler_ReceivesContext_WithKeyAndDtoType()
    {
        var (sp, _, handler) = BuildScope(lastSuccess: null);

        var pipeline = new SyncPipeline<IFakeClient, Dto>(Key, (c, since, ct) => c.FetchAsync(since, ct));
        await pipeline.ExecuteAsync(sp, default);

        var ctx = handler.LastContext;
        Assert.NotNull(ctx);
        Assert.Equal(Key, ctx!.Key);
        Assert.Equal(typeof(Dto), ctx.DtoType);
    }

    [Fact]
    public void Ctor_RejectsEmptyProviderOrStream()
    {
        Assert.Throws<ArgumentException>(() => new SyncPipeline<IFakeClient, Dto>(new SyncStateKey("", "s"), (_, _, _) => Task.FromResult<IReadOnlyCollection<Dto>>(Array.Empty<Dto>())));
        Assert.Throws<ArgumentException>(() => new SyncPipeline<IFakeClient, Dto>(new SyncStateKey("p", ""), (_, _, _) => Task.FromResult<IReadOnlyCollection<Dto>>(Array.Empty<Dto>())));
    }

    private static (IServiceProvider sp, FakeClient client, FakeHandler handler) BuildScope(DateTime? lastSuccess)
    {
        var services = new ServiceCollection();
        var store = new InMemorySyncStateStore();
        if (lastSuccess is { } t)
        {
            store.RecordSuccessAsync(Key, t, TimeSpan.FromSeconds(1), recordCount: 5).GetAwaiter().GetResult();
        }
        services.AddSingleton<ISyncStateStore>(store);

        var client = new FakeClient();
        services.AddSingleton<IFakeClient>(client);

        var handler = new FakeHandler();
        services.AddSingleton<ISyncDataHandler<Dto>>(handler);
        return (services.BuildServiceProvider(), client, handler);
    }

    public sealed record Dto(int N);

    public interface IFakeClient
    {
        Task<IReadOnlyCollection<Dto>> FetchAsync(DateTime? since, CancellationToken cancellationToken);
    }

    private sealed class FakeClient : IFakeClient
    {
        public DateTime? LastFetchSince { get; private set; }

        public Task<IReadOnlyCollection<Dto>> FetchAsync(DateTime? since, CancellationToken cancellationToken)
        {
            LastFetchSince = since;
            return Task.FromResult<IReadOnlyCollection<Dto>>([new Dto(1), new Dto(2)]);
        }
    }

    private sealed class FakeHandler : ISyncDataHandler<Dto>
    {
        public List<IReadOnlyCollection<Dto>> HandledBatches { get; } = new();
        public SyncContext? LastContext { get; private set; }

        public Task HandleAsync(SyncContext context, IReadOnlyCollection<Dto> data, CancellationToken cancellationToken = default)
        {
            LastContext = context;
            HandledBatches.Add(data);
            return Task.CompletedTask;
        }
    }
}
