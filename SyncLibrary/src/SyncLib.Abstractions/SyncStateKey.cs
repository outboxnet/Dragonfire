namespace SyncLib.Abstractions;

/// <summary>
/// Composite identity for a sync stream. A single provider (e.g. an API)
/// commonly serves several streams, one per DTO type — for example a
/// weather API serving both <c>WeatherDto</c> and <c>AlertDto</c> as separate
/// streams.
/// </summary>
/// <param name="ProviderName">Logical name of the upstream system / API client.</param>
/// <param name="StreamName">Sub-key under the provider — typically the DTO type name.</param>
public readonly record struct SyncStateKey(string ProviderName, string StreamName)
{
    /// <summary>Stable string form, suitable for logs, dictionary keys and tags.</summary>
    public override string ToString() => $"{ProviderName}/{StreamName}";
}
