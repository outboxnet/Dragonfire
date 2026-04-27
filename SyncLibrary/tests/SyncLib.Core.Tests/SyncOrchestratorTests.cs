using Microsoft.Extensions.DependencyInjection;
using SyncLib.Abstractions;
using SyncLib.Core;
using SyncLib.Core.DependencyInjection;
using Xunit;

namespace SyncLib.Core.Tests;

public class SyncOrchestratorTests
{
    private static readonly SyncStateKey FakeKey = new("fake", "default");
    private static readonly SyncStateKey FlakyKey = new("flaky", "default");
    private static readonly SyncStateKey BrokenKey = new("broken", "default");

    [Fact]
    public async Task TriggerManualSync_RunsStream_AndRecordsSuccess()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSyncLibrary();
        services.AddSingleton<IFakeClient, FakeClient>();
        services.AddSyncStream<IFakeClient, FakeDto>(FakeKey.ProviderName, FakeKey.StreamName)
            .WithFetch((c, since, ct) => c.FetchAsync(since, ct))
            .HandledBy<FakeHandler>()
            .Configure(c => c.MaxRetryAttempts = 0)
            .Build();
        services.AddScoped<FakeHandler>();

        await using var sp = services.BuildServiceProvider();
        var orch = sp.GetRequiredService<ISyncOrchestrator>();

        await orch.TriggerManualSyncAsync(FakeKey);

        var state = await sp.GetRequiredService<ISyncStateReader>().GetAsync(FakeKey);
        Assert.NotNull(state);
        Assert.Equal(SyncStatus.Succeeded, state!.LastStatus);
        Assert.Equal(3, state.LastRecordCount);
    }

    [Fact]
    public async Task TriggerManualSync_RetriesUntilSuccess()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSyncLibrary();
        services.AddSingleton<IFlakyClient, FlakyClient>();
        services.AddSyncStream<IFlakyClient, FakeDto>(FlakyKey.ProviderName, FlakyKey.StreamName)
            .WithFetch((c, since, ct) => c.FetchAsync(since, ct))
            .HandledBy<FakeHandler>()
            .Configure(c =>
            {
                c.MaxRetryAttempts = 2;
                c.RetryDelayBase = TimeSpan.FromMilliseconds(1);
            })
            .Build();
        services.AddScoped<FakeHandler>();

        await using var sp = services.BuildServiceProvider();
        await sp.GetRequiredService<ISyncOrchestrator>().TriggerManualSyncAsync(FlakyKey);

        var state = await sp.GetRequiredService<ISyncStateReader>().GetAsync(FlakyKey);
        Assert.Equal(SyncStatus.Succeeded, state!.LastStatus);
    }

    [Fact]
    public async Task TriggerManualSync_RecordsFailure_AfterRetriesExhausted()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSyncLibrary();
        services.AddSingleton<IBrokenClient, BrokenClient>();
        services.AddSyncStream<IBrokenClient, FakeDto>(BrokenKey.ProviderName, BrokenKey.StreamName)
            .WithFetch((c, since, ct) => c.FetchAsync(since, ct))
            .HandledBy<FakeHandler>()
            .Configure(c =>
            {
                c.MaxRetryAttempts = 1;
                c.RetryDelayBase = TimeSpan.FromMilliseconds(1);
                c.EnableCircuitBreaker = false;
            })
            .Build();
        services.AddScoped<FakeHandler>();

        await using var sp = services.BuildServiceProvider();
        await sp.GetRequiredService<ISyncOrchestrator>().TriggerManualSyncAsync(BrokenKey);

        var state = await sp.GetRequiredService<ISyncStateReader>().GetAsync(BrokenKey);
        Assert.Equal(SyncStatus.Failed, state!.LastStatus);
        Assert.Equal(1, state.TotalFailures);
        Assert.NotNull(state.LastError);
    }

    [Fact]
    public async Task RunAllAsync_RunsEveryStream_AndAggregatesOutcomes()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSyncRunner();
        services.AddSingleton<IFakeClient, FakeClient>();
        services.AddSingleton<IBrokenClient, BrokenClient>();

        services.AddSyncStream<IFakeClient, FakeDto>("ok", "default")
            .WithFetch((c, since, ct) => c.FetchAsync(since, ct))
            .HandledBy<FakeHandler>()
            .Configure(c => c.MaxRetryAttempts = 0)
            .Build();

        services.AddSyncStream<IBrokenClient, OtherDto>("bad", "default")
            .WithFetch((c, since, ct) => c.FetchOtherAsync(since, ct))
            .HandledBy<OtherHandler>()
            .Configure(c =>
            {
                c.MaxRetryAttempts = 0;
                c.EnableCircuitBreaker = false;
                c.RetryDelayBase = TimeSpan.FromMilliseconds(1);
            })
            .Build();

        services.AddScoped<FakeHandler>();
        services.AddScoped<OtherHandler>();

        await using var sp = services.BuildServiceProvider();
        var runner = sp.GetRequiredService<ISyncRunner>();

        var summary = await runner.RunAllAsync();

        Assert.Equal(2, summary.TotalStreams);
        Assert.Equal(1, summary.Succeeded);
        Assert.Equal(1, summary.Failed);
        Assert.False(summary.IsHealthy);
        Assert.Contains(new SyncStateKey("bad", "default"), summary.StreamErrors.Keys);
    }

    [Fact]
    public async Task RunProviderAsync_OnlyRunsStreamsForOneProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSyncRunner();
        services.AddSingleton<IFakeClient, FakeClient>();

        services.AddSyncStream<IFakeClient, FakeDto>("weather", "forecasts")
            .WithFetch((c, since, ct) => c.FetchAsync(since, ct))
            .HandledBy<FakeHandler>()
            .Configure(c => c.MaxRetryAttempts = 0)
            .Build();

        services.AddSyncStream<IFakeClient, FakeDto>("traffic", "incidents")
            .WithFetch((c, since, ct) => c.FetchAsync(since, ct))
            .HandledBy<FakeHandler>()
            .Configure(c => c.MaxRetryAttempts = 0)
            .Build();

        services.AddScoped<FakeHandler>();

        await using var sp = services.BuildServiceProvider();
        var summary = await sp.GetRequiredService<ISyncRunner>().RunProviderAsync("weather");

        Assert.Equal(1, summary.TotalStreams);
        Assert.Equal(1, summary.Succeeded);

        var all = await sp.GetRequiredService<ISyncStateReader>().GetAllAsync();
        Assert.Single(all); // only the "weather" stream ran
    }

    [Fact]
    public async Task TriggerManualSync_Throws_ForUnknownStream()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSyncLibrary();

        await using var sp = services.BuildServiceProvider();
        var orch = sp.GetRequiredService<ISyncOrchestrator>();

        await Assert.ThrowsAsync<ArgumentException>(() => orch.TriggerManualSyncAsync(new SyncStateKey("nope", "default")));
    }

    // ---- Test doubles --------------------------------------------------

    public sealed record FakeDto(int N);
    public sealed record OtherDto(string S);

    public interface IFakeClient
    {
        Task<IReadOnlyCollection<FakeDto>> FetchAsync(DateTime? since, CancellationToken cancellationToken);
    }

    public interface IFlakyClient
    {
        Task<IReadOnlyCollection<FakeDto>> FetchAsync(DateTime? since, CancellationToken cancellationToken);
    }

    public interface IBrokenClient
    {
        Task<IReadOnlyCollection<FakeDto>> FetchAsync(DateTime? since, CancellationToken cancellationToken);
        Task<IReadOnlyCollection<OtherDto>> FetchOtherAsync(DateTime? since, CancellationToken cancellationToken);
    }

    private sealed class FakeClient : IFakeClient
    {
        public Task<IReadOnlyCollection<FakeDto>> FetchAsync(DateTime? since, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<FakeDto>>(new[] { new FakeDto(1), new FakeDto(2), new FakeDto(3) });
    }

    private sealed class FlakyClient : IFlakyClient
    {
        private int _calls;
        public Task<IReadOnlyCollection<FakeDto>> FetchAsync(DateTime? since, CancellationToken cancellationToken)
        {
            _calls++;
            if (_calls < 2) throw new InvalidOperationException("transient");
            return Task.FromResult<IReadOnlyCollection<FakeDto>>(new[] { new FakeDto(1) });
        }
    }

    private sealed class BrokenClient : IBrokenClient
    {
        public Task<IReadOnlyCollection<FakeDto>> FetchAsync(DateTime? since, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("nope");
        public Task<IReadOnlyCollection<OtherDto>> FetchOtherAsync(DateTime? since, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("nope");
    }

    private sealed class FakeHandler : ISyncDataHandler<FakeDto>
    {
        public Task HandleAsync(SyncContext context, IReadOnlyCollection<FakeDto> data, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class OtherHandler : ISyncDataHandler<OtherDto>
    {
        public Task HandleAsync(SyncContext context, IReadOnlyCollection<OtherDto> data, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
