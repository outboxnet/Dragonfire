using SyncLib.Abstractions;
using SyncLib.Core;
using Xunit;

namespace SyncLib.Core.Tests;

public class InMemorySyncStateStoreTests
{
    private static readonly SyncStateKey KeyP = new("p", "default");
    private static readonly SyncStateKey KeyA = new("a", "default");
    private static readonly SyncStateKey KeyB = new("b", "default");

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenStreamUnknown()
    {
        var store = new InMemorySyncStateStore();
        Assert.Null(await store.GetAsync(new SyncStateKey("missing", "default")));
    }

    [Fact]
    public async Task RecordSuccess_PopulatesRecordAndIncrementsCounter()
    {
        var store = new InMemorySyncStateStore();
        var startedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await store.RecordRunStartedAsync(KeyP, startedAt);
        await store.RecordSuccessAsync(KeyP, startedAt, TimeSpan.FromSeconds(2), recordCount: 7);

        var state = await store.GetAsync(KeyP);
        Assert.NotNull(state);
        Assert.Equal(SyncStatus.Succeeded, state!.LastStatus);
        Assert.Equal(startedAt, state.LastSuccessAt);
        Assert.Equal(TimeSpan.FromSeconds(2), state.LastDuration);
        Assert.Equal(7, state.LastRecordCount);
        Assert.Equal(1, state.TotalSuccesses);
        Assert.Equal(0, state.ConsecutiveFailures);
    }

    [Fact]
    public async Task RecordFailure_IncrementsConsecutiveFailures_AndStoresMessage()
    {
        var store = new InMemorySyncStateStore();
        var t = DateTime.UtcNow;
        await store.RecordFailureAsync(KeyP, t, TimeSpan.FromMilliseconds(50), new InvalidOperationException("boom"));
        await store.RecordFailureAsync(KeyP, t, TimeSpan.FromMilliseconds(50), new InvalidOperationException("boom2"));

        var state = await store.GetAsync(KeyP);
        Assert.Equal(2, state!.TotalFailures);
        Assert.Equal(2, state.ConsecutiveFailures);
        Assert.Equal("boom2", state.LastError);
    }

    [Fact]
    public async Task SuccessAfterFailure_ResetsConsecutiveCounter()
    {
        var store = new InMemorySyncStateStore();
        var t = DateTime.UtcNow;
        await store.RecordFailureAsync(KeyP, t, TimeSpan.Zero, new Exception("x"));
        await store.RecordSuccessAsync(KeyP, t, TimeSpan.FromMilliseconds(1), 0);

        var state = await store.GetAsync(KeyP);
        Assert.Equal(0, state!.ConsecutiveFailures);
        Assert.Null(state.LastError);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllStreams()
    {
        var store = new InMemorySyncStateStore();
        await store.RecordRunStartedAsync(KeyA, DateTime.UtcNow);
        await store.RecordRunStartedAsync(KeyB, DateTime.UtcNow);

        var all = await store.GetAllAsync();
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task GetByProviderAsync_ScopesToOneProvider()
    {
        var store = new InMemorySyncStateStore();
        await store.RecordRunStartedAsync(new SyncStateKey("p", "forecasts"), DateTime.UtcNow);
        await store.RecordRunStartedAsync(new SyncStateKey("p", "alerts"), DateTime.UtcNow);
        await store.RecordRunStartedAsync(new SyncStateKey("other", "forecasts"), DateTime.UtcNow);

        var forP = await store.GetByProviderAsync("p");
        Assert.Equal(2, forP.Count);
        Assert.All(forP, r => Assert.Equal("p", r.Key.ProviderName));
    }

    [Fact]
    public async Task SameProvider_DifferentStreams_AreTrackedIndependently()
    {
        var store = new InMemorySyncStateStore();
        var forecasts = new SyncStateKey("weather", "forecasts");
        var alerts = new SyncStateKey("weather", "alerts");
        var t = DateTime.UtcNow;

        await store.RecordSuccessAsync(forecasts, t, TimeSpan.FromMilliseconds(1), 5);
        await store.RecordFailureAsync(alerts, t, TimeSpan.FromMilliseconds(1), new Exception("alert-boom"));

        var f = await store.GetAsync(forecasts);
        var a = await store.GetAsync(alerts);
        Assert.Equal(SyncStatus.Succeeded, f!.LastStatus);
        Assert.Equal(SyncStatus.Failed, a!.LastStatus);
        Assert.Equal("alert-boom", a.LastError);
    }
}
