namespace Dragonfire.TenantContext.Resolution;

/// <summary>
/// Thrown when tenant resolution fails under the configured <see cref="TenantResolutionOptions"/>
/// (missing tenant with <see cref="MissingTenantPolicy.Throw"/>, or ambiguous resolution
/// with <see cref="AmbiguityPolicy.Throw"/>).
/// </summary>
public sealed class TenantResolutionException : Exception
{
    public TenantResolutionException(string message) : base(message) { }
    public TenantResolutionException(string message, Exception inner) : base(message, inner) { }

    /// <summary>Source/value pairs that participated in the failed resolution (best-effort, may be empty).</summary>
    public IReadOnlyList<(string Source, string TenantId)> Candidates { get; init; } = Array.Empty<(string, string)>();
}
