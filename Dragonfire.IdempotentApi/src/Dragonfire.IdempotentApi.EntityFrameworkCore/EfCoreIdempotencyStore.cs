using System.Text.Json;
using Dragonfire.IdempotentApi.Interfaces;
using Dragonfire.IdempotentApi.Models;
using Microsoft.EntityFrameworkCore;

namespace Dragonfire.IdempotentApi.EntityFrameworkCore;

/// <summary>
/// EF Core-backed <see cref="IIdempotencyStore"/>. Atomicity for <see cref="TryReserveAsync"/>
/// is provided by the unique primary key on <see cref="IdempotencyRecord.Key"/>: concurrent
/// reservations of the same key fail at <c>SaveChanges</c> for all but the winner, who is
/// distinguished from losers by a re-read.
/// </summary>
public sealed class EfCoreIdempotencyStore<TContext> : IIdempotencyStore
    where TContext : DbContext
{
    private readonly IDbContextFactory<TContext> _factory;
    private readonly Func<TContext, DbSet<IdempotencyRecord>> _setSelector;
    private readonly TimeProvider _time;

    public EfCoreIdempotencyStore(
        IDbContextFactory<TContext> factory,
        Func<TContext, DbSet<IdempotencyRecord>> setSelector,
        TimeProvider? timeProvider = null)
    {
        _factory = factory;
        _setSelector = setSelector;
        _time = timeProvider ?? TimeProvider.System;
    }

    public async Task<ReservationOutcome> TryReserveAsync(
        string key, string fingerprint, DateTimeOffset expiresAt, CancellationToken ct)
    {
        var now = _time.GetUtcNow();

        await using var db = await _factory.CreateDbContextAsync(ct);
        var set = _setSelector(db);

        var existing = await set.AsTracking().FirstOrDefaultAsync(r => r.Key == key, ct);
        if (existing is not null)
        {
            if (existing.ExpiresAt < now)
            {
                set.Remove(existing);
                await db.SaveChangesAsync(ct);
                // fall through to the insert below
            }
            else
            {
                if (!string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
                    return ReservationOutcome.FingerprintMismatch(ToDomain(existing));

                return existing.Status == IdempotencyStatus.Completed
                    ? ReservationOutcome.AlreadyCompleted(ToDomain(existing))
                    : ReservationOutcome.InProgress(ToDomain(existing));
            }
        }

        var record = new IdempotencyRecord
        {
            Key         = key,
            Fingerprint = fingerprint,
            Status      = IdempotencyStatus.Reserved,
            CreatedAt   = now,
            ExpiresAt   = expiresAt,
        };

        try
        {
            set.Add(record);
            await db.SaveChangesAsync(ct);
            return ReservationOutcome.Acquired();
        }
        catch (DbUpdateException)
        {
            // Lost the insert race — re-read the winner and report an existing entry.
            await using var db2 = await _factory.CreateDbContextAsync(ct);
            var winner = await _setSelector(db2).FirstOrDefaultAsync(r => r.Key == key, ct);
            if (winner is null)
                throw;

            if (!string.Equals(winner.Fingerprint, fingerprint, StringComparison.Ordinal))
                return ReservationOutcome.FingerprintMismatch(ToDomain(winner));

            return winner.Status == IdempotencyStatus.Completed
                ? ReservationOutcome.AlreadyCompleted(ToDomain(winner))
                : ReservationOutcome.InProgress(ToDomain(winner));
        }
    }

    public async Task SaveResponseAsync(string key, IdempotentResponse response, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var set = _setSelector(db);

        var record = await set.AsTracking().FirstOrDefaultAsync(r => r.Key == key, ct)
            ?? throw new InvalidOperationException($"No reservation found for idempotency key '{key}'.");

        record.Status = IdempotencyStatus.Completed;
        record.StatusCode = response.StatusCode;
        record.ContentType = response.ContentType;
        record.ResponseBody = response.Body;
        record.ResponseHeadersJson = JsonSerializer.Serialize(response.Headers);

        await db.SaveChangesAsync(ct);
    }

    public async Task ReleaseReservationAsync(string key, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var set = _setSelector(db);

        // Only Reserved rows are eligible — completed rows must be preserved for replay.
        var record = await set.AsTracking()
            .FirstOrDefaultAsync(r => r.Key == key && r.Status == IdempotencyStatus.Reserved, ct);
        if (record is null) return;

        set.Remove(record);
        await db.SaveChangesAsync(ct);
    }

    private static IdempotencyEntry ToDomain(IdempotencyRecord r) => new()
    {
        Key         = r.Key,
        Fingerprint = r.Fingerprint,
        Status      = r.Status,
        CreatedAt   = r.CreatedAt,
        ExpiresAt   = r.ExpiresAt,
        Response    = r.Status == IdempotencyStatus.Completed && r.StatusCode is not null
            ? new IdempotentResponse
            {
                StatusCode  = r.StatusCode.Value,
                ContentType = r.ContentType,
                Body        = r.ResponseBody ?? Array.Empty<byte>(),
                Headers     = string.IsNullOrEmpty(r.ResponseHeadersJson)
                    ? new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                    : (JsonSerializer.Deserialize<Dictionary<string, string[]>>(r.ResponseHeadersJson)
                        ?? new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)),
            }
            : null,
    };
}
