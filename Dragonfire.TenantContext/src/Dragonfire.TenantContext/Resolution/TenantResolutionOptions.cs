namespace Dragonfire.TenantContext.Resolution;

/// <summary>
/// Behavior knobs applied by <see cref="CompositeTenantResolver"/> when running the
/// configured resolver chain.
/// </summary>
public sealed class TenantResolutionOptions
{
    /// <summary>What to do when no resolver produces a tenant. Defaults to <see cref="MissingTenantPolicy.AllowEmpty"/>.</summary>
    public MissingTenantPolicy OnMissing { get; set; } = MissingTenantPolicy.AllowEmpty;

    /// <summary>What to do when multiple resolvers produce conflicting tenant ids. Defaults to <see cref="AmbiguityPolicy.UseFirst"/>.</summary>
    public AmbiguityPolicy OnAmbiguous { get; set; } = AmbiguityPolicy.UseFirst;

    /// <summary>
    /// Tenant id used when <see cref="OnMissing"/> is <see cref="MissingTenantPolicy.UseDefault"/>.
    /// Required (non-empty) in that mode; ignored otherwise.
    /// </summary>
    public TenantId DefaultTenant { get; set; } = TenantId.Empty;

    /// <summary>
    /// When <c>true</c>, the resolver chain stops as soon as one resolver returns a value (fast path).
    /// When <c>false</c>, all resolvers are invoked so ambiguity detection can run.
    /// Default: <c>true</c>.
    /// </summary>
    public bool ShortCircuitOnFirstMatch { get; set; } = true;

    /// <summary>
    /// String comparison used when comparing tenant ids across resolvers for ambiguity detection.
    /// Defaults to <see cref="StringComparer.OrdinalIgnoreCase"/>.
    /// </summary>
    public StringComparer TenantIdComparer { get; set; } = StringComparer.OrdinalIgnoreCase;

    internal void Validate()
    {
        if (OnMissing == MissingTenantPolicy.UseDefault && DefaultTenant.IsEmpty)
            throw new InvalidOperationException(
                $"{nameof(TenantResolutionOptions)}.{nameof(DefaultTenant)} must be set when {nameof(OnMissing)} is {nameof(MissingTenantPolicy.UseDefault)}.");
    }
}

/// <summary>Policy applied when no resolver returns a tenant.</summary>
public enum MissingTenantPolicy
{
    /// <summary>Leave the ambient tenant empty. Downstream code must handle <see cref="TenantInfo.None"/>.</summary>
    AllowEmpty = 0,
    /// <summary>Throw <see cref="TenantResolutionException"/>.</summary>
    Throw = 1,
    /// <summary>Use <see cref="TenantResolutionOptions.DefaultTenant"/>.</summary>
    UseDefault = 2,
}

/// <summary>Policy applied when resolvers produce conflicting tenant ids.</summary>
public enum AmbiguityPolicy
{
    /// <summary>Pick the first resolver's value (insertion order).</summary>
    UseFirst = 0,
    /// <summary>Throw <see cref="TenantResolutionException"/> when resolvers disagree.</summary>
    Throw = 1,
}
