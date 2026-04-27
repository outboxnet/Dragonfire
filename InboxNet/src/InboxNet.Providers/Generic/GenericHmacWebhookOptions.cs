namespace InboxNet.Providers.Generic;

public sealed class GenericHmacWebhookOptions
{
    /// <summary>
    /// Provider routing key — must be unique across registered providers.
    /// </summary>
    public string Key { get; set; } = "generic";

    /// <summary>
    /// Shared HMAC-SHA256 secret.
    /// </summary>
    public string Secret { get; set; } = default!;

    /// <summary>
    /// Header containing the hex-encoded HMAC-SHA256 signature of the raw body.
    /// Defaults to <c>X-Signature-256</c>.
    /// </summary>
    public string SignatureHeader { get; set; } = "X-Signature-256";

    /// <summary>
    /// Optional header prefix (e.g. <c>"sha256="</c>). Empty by default.
    /// </summary>
    public string SignaturePrefix { get; set; } = "";

    /// <summary>
    /// Header containing the event type. When null, the event type is read from
    /// <see cref="EventTypeJsonProperty"/> on the parsed body.
    /// </summary>
    public string? EventTypeHeader { get; set; }

    /// <summary>
    /// JSON property used as the event type when <see cref="EventTypeHeader"/> is null.
    /// Defaults to <c>"type"</c>.
    /// </summary>
    public string EventTypeJsonProperty { get; set; } = "type";

    /// <summary>
    /// Optional header carrying a provider-supplied event ID used for dedup. When
    /// null, dedup falls back to the SHA-256 of the body.
    /// </summary>
    public string? EventIdHeader { get; set; }

    /// <summary>
    /// Optional JSON property holding a stable entity ID for ordered processing.
    /// </summary>
    public string? EntityIdJsonProperty { get; set; }

    /// <summary>
    /// Optional header carrying a tenant identifier.
    /// </summary>
    public string? TenantIdHeader { get; set; }
}
