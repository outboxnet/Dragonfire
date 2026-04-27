namespace SyncLib.Abstractions;

/// <summary>
/// Per-run context passed to the fetch delegate and the
/// <see cref="ISyncDataHandler{TDto}"/>. Provides the identity of the stream
/// being executed and incremental-fetch hints.
/// </summary>
public sealed record SyncContext
{
    /// <summary>Composite identity of the stream being run.</summary>
    public required SyncStateKey Key { get; init; }

    /// <summary>The DTO type being synced; equal to <c>typeof(TDto)</c> in <see cref="ISyncDataHandler{TDto}"/>.</summary>
    public required Type DtoType { get; init; }

    /// <summary>UTC start time of the current run (set by the runner before fetch).</summary>
    public required DateTime StartedAtUtc { get; init; }

    /// <summary>UTC start time of the most recent successful run for this stream, if any.</summary>
    public DateTime? LastSuccessAt { get; init; }

    /// <summary>Convenience accessor for the provider name portion of <see cref="Key"/>.</summary>
    public string ProviderName => Key.ProviderName;

    /// <summary>Convenience accessor for the stream name portion of <see cref="Key"/>.</summary>
    public string StreamName => Key.StreamName;
}
