using Dragonfire.IdempotentApi.InMemory;
using Dragonfire.IdempotentApi.Models;
using FluentAssertions;
using Xunit;

namespace Dragonfire.IdempotentApi.Tests;

public class InMemoryIdempotencyStoreTests
{
    private static DateTimeOffset Future => DateTimeOffset.UtcNow.AddMinutes(10);

    [Fact]
    public async Task First_request_acquires()
    {
        var store = new InMemoryIdempotencyStore();
        var outcome = await store.TryReserveAsync("k1", "fp", Future, default);

        outcome.Kind.Should().Be(ReservationKind.Acquired);
    }

    [Fact]
    public async Task Same_key_while_reserved_returns_in_progress()
    {
        var store = new InMemoryIdempotencyStore();
        await store.TryReserveAsync("k", "fp", Future, default);

        var second = await store.TryReserveAsync("k", "fp", Future, default);

        second.Kind.Should().Be(ReservationKind.InProgress);
    }

    [Fact]
    public async Task Same_key_after_completion_replays_response()
    {
        var store = new InMemoryIdempotencyStore();
        await store.TryReserveAsync("k", "fp", Future, default);
        await store.SaveResponseAsync("k", new IdempotentResponse { StatusCode = 201 }, default);

        var second = await store.TryReserveAsync("k", "fp", Future, default);

        second.Kind.Should().Be(ReservationKind.AlreadyCompleted);
        second.Entry!.Response!.StatusCode.Should().Be(201);
    }

    [Fact]
    public async Task Same_key_with_different_fingerprint_returns_mismatch()
    {
        var store = new InMemoryIdempotencyStore();
        await store.TryReserveAsync("k", "fp1", Future, default);

        var second = await store.TryReserveAsync("k", "fp2", Future, default);

        second.Kind.Should().Be(ReservationKind.FingerprintMismatch);
    }

    [Fact]
    public async Task Released_reservation_can_be_reacquired()
    {
        var store = new InMemoryIdempotencyStore();
        await store.TryReserveAsync("k", "fp", Future, default);
        await store.ReleaseReservationAsync("k", default);

        var second = await store.TryReserveAsync("k", "fp", Future, default);

        second.Kind.Should().Be(ReservationKind.Acquired);
    }

    [Fact]
    public async Task Release_does_not_remove_completed_entry()
    {
        var store = new InMemoryIdempotencyStore();
        await store.TryReserveAsync("k", "fp", Future, default);
        await store.SaveResponseAsync("k", new IdempotentResponse { StatusCode = 200 }, default);

        await store.ReleaseReservationAsync("k", default);

        var second = await store.TryReserveAsync("k", "fp", Future, default);
        second.Kind.Should().Be(ReservationKind.AlreadyCompleted);
    }

    [Fact]
    public async Task Expired_entry_is_reclaimable()
    {
        var store = new InMemoryIdempotencyStore();
        await store.TryReserveAsync("k", "fp", DateTimeOffset.UtcNow.AddSeconds(-1), default);

        var second = await store.TryReserveAsync("k", "fp", Future, default);

        second.Kind.Should().Be(ReservationKind.Acquired);
    }

    [Fact]
    public async Task Save_response_without_reservation_throws()
    {
        var store = new InMemoryIdempotencyStore();

        var act = () => store.SaveResponseAsync("missing", new IdempotentResponse(), default);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
