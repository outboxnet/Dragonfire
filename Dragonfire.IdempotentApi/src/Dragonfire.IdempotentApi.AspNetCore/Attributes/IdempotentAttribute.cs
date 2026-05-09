namespace Dragonfire.IdempotentApi.Attributes;

/// <summary>
/// Opt-in marker for endpoint-attribute-based policies. Apply to a controller action,
/// minimal-API endpoint, or controller class to make it idempotency-managed.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class IdempotentAttribute : Attribute
{
    /// <summary>Optional override for <see cref="Options.IdempotentApiOptions.DefaultExpiration"/>.</summary>
    public TimeSpan? Expiration { get; init; }
}
