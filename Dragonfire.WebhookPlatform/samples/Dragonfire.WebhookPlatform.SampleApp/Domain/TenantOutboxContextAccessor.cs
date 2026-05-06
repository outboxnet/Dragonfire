using Dragonfire.Outbox.Interfaces;
using Dragonfire.TenantContext;

namespace Dragonfire.WebhookPlatform.SampleApp.Domain;

/// <summary>
/// Bridges <see cref="ITenantContextAccessor"/> (resolved by Dragonfire.TenantContext middleware
/// from the X-Tenant-Id header) into the outbox so every published event is automatically tagged
/// with the request's TenantId — no caller plumbing required.
/// </summary>
public sealed class TenantOutboxContextAccessor : IOutboxContextAccessor
{
    private readonly ITenantContextAccessor _tenant;

    public TenantOutboxContextAccessor(ITenantContextAccessor tenant) => _tenant = tenant;

    public string? TenantId
    {
        get
        {
            var current = _tenant.Current;
            return current.IsResolved ? current.TenantId.Value : null;
        }
    }

    // Sample app has no auth — leave UserId empty. A real app would read this from
    // ClaimsPrincipal or a user accessor.
    public string? UserId => null;
}
