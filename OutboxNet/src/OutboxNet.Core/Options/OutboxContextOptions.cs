namespace OutboxNet.Options;

/// <summary>
/// Options for the HTTP-context-based outbox context accessor.
/// Configure which claim types map to TenantId and UserId.
/// </summary>
public class OutboxContextOptions
{
    /// <summary>
    /// The claim type used to resolve the current tenant ID from the HTTP context.
    /// When null, TenantId will not be populated automatically.
    /// </summary>
    public string? TenantIdClaimType { get; set; }

    /// <summary>
    /// The claim type used to resolve the current user ID from the HTTP context.
    /// When null, UserId will not be populated automatically.
    /// </summary>
    public string? UserIdClaimType { get; set; }
}
