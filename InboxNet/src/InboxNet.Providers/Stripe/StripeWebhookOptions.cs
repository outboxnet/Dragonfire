namespace InboxNet.Providers.Stripe;

public sealed class StripeWebhookOptions
{
    /// <summary>
    /// Stripe webhook signing secret (starts with <c>whsec_</c>). Find under
    /// Dashboard → Developers → Webhooks → {endpoint} → Signing secret.
    /// </summary>
    public string SigningSecret { get; set; } = default!;

    /// <summary>
    /// Maximum allowed clock skew between Stripe and the receiver, in seconds.
    /// Requests older than this are rejected as a replay defence. Default: 300 s.
    /// </summary>
    public int ToleranceSeconds { get; set; } = 300;

    /// <summary>
    /// Provider routing key. Defaults to <c>"stripe"</c>.
    /// </summary>
    public string Key { get; set; } = "stripe";
}
