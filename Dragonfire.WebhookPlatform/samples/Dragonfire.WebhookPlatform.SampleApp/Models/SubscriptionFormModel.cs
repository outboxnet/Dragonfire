using System.ComponentModel.DataAnnotations;

namespace Dragonfire.WebhookPlatform.SampleApp.Models;

/// <summary>
/// Backs the Subscriptions Create/Edit forms. Mirrors <c>WebhookSubscription</c> but uses
/// primitive types so model binding from a plain HTML form just works (TimeSpan is exposed
/// as <c>TimeoutSeconds</c>; the headers Dictionary is exposed as a single
/// <c>X-Subscription-Name</c> field).
/// </summary>
public sealed class SubscriptionFormModel
{
    public Guid? Id { get; set; }

    [Display(Name = "Name")]
    [StringLength(120)]
    public string? Name { get; set; }

    [Required, Url]
    [Display(Name = "Webhook URL")]
    public string WebhookUrl { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Event type (use * to match every event)")]
    [StringLength(256)]
    public string EventType { get; set; } = "*";

    [Required, StringLength(256, MinimumLength = 8)]
    [Display(Name = "Shared secret (HMAC-SHA256)")]
    public string Secret { get; set; } = string.Empty;

    [Display(Name = "Tenant id (leave blank for all tenants)")]
    [StringLength(64)]
    public string? TenantId { get; set; }

    [Range(0, 50)]
    [Display(Name = "Max retries")]
    public int MaxRetries { get; set; } = 5;

    [Range(1, 600)]
    [Display(Name = "Timeout (seconds)")]
    public int TimeoutSeconds { get; set; } = 30;

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}
