using Microsoft.EntityFrameworkCore;
using SyncLib.Abstractions;

namespace SyncLib.Sample.WebApi.Sync;

/// <summary>
/// Persists forecasts via EF Core. Notice the app — not the library — decides
/// how DTOs map to entities and how upserts are performed. SyncContext gives
/// the handler the provider/stream identity if it needs to fan out per-source.
/// </summary>
public sealed class ForecastHandler : ISyncDataHandler<ForecastDto>
{
    private readonly AppDbContext _db;
    private readonly ILogger<ForecastHandler> _logger;

    public ForecastHandler(AppDbContext db, ILogger<ForecastHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task HandleAsync(SyncContext context, IReadOnlyCollection<ForecastDto> data, CancellationToken cancellationToken = default)
    {
        if (data.Count == 0) return;

        var now = DateTime.UtcNow;
        var existingByCity = await _db.Forecasts
            .Where(e => data.Select(d => d.City).Contains(e.City))
            .ToDictionaryAsync(e => e.City, cancellationToken).ConfigureAwait(false);

        foreach (var dto in data)
        {
            if (existingByCity.TryGetValue(dto.City, out var entity))
            {
                entity.TemperatureC = dto.TemperatureC;
                entity.ObservedAt = dto.ObservedAt;
                entity.UpdatedAt = now;
            }
            else
            {
                _db.Forecasts.Add(new ForecastEntity
                {
                    Id = Guid.NewGuid(),
                    City = dto.City,
                    TemperatureC = dto.TemperatureC,
                    ObservedAt = dto.ObservedAt,
                    CreatedAt = now
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Persisted {Count} forecasts for {Stream}", data.Count, context.Key);
    }
}

/// <summary>Persists alerts; appends only — alerts are immutable once issued.</summary>
public sealed class AlertHandler : ISyncDataHandler<AlertDto>
{
    private readonly AppDbContext _db;
    private readonly ILogger<AlertHandler> _logger;

    public AlertHandler(AppDbContext db, ILogger<AlertHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task HandleAsync(SyncContext context, IReadOnlyCollection<AlertDto> data, CancellationToken cancellationToken = default)
    {
        if (data.Count == 0) return;

        var now = DateTime.UtcNow;
        foreach (var dto in data)
        {
            _db.Alerts.Add(new AlertEntity
            {
                Id = Guid.NewGuid(),
                City = dto.City,
                Severity = dto.Severity,
                Message = dto.Message,
                IssuedAt = dto.IssuedAt,
                CreatedAt = now
            });
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Persisted {Count} alerts for {Stream}", data.Count, context.Key);
    }
}
