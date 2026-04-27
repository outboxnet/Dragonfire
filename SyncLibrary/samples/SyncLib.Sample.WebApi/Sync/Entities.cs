namespace SyncLib.Sample.WebApi.Sync;

/// <summary>App-owned database entity for forecasts. Independent of the DTO shape.</summary>
public sealed class ForecastEntity
{
    public Guid Id { get; set; }
    public string City { get; set; } = null!;
    public double TemperatureC { get; set; }
    public DateTime ObservedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>App-owned database entity for alerts.</summary>
public sealed class AlertEntity
{
    public Guid Id { get; set; }
    public string City { get; set; } = null!;
    public string Severity { get; set; } = null!;
    public string Message { get; set; } = null!;
    public DateTime IssuedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
