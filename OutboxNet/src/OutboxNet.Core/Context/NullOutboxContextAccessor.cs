using OutboxNet.Interfaces;

namespace OutboxNet.Context;

/// <summary>
/// Default no-op implementation that returns null for both TenantId and UserId.
/// Replace with <see cref="HttpContextOutboxContextAccessor"/> (or a custom implementation)
/// by calling <c>UseHttpContextAccessor()</c> on the outbox builder.
/// </summary>
public sealed class NullOutboxContextAccessor : IOutboxContextAccessor
{
    public string? TenantId => null;
    public string? UserId => null;
}
