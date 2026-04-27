using Microsoft.AspNetCore.Http;

namespace Dragonfire.TenantContext.AspNetCore;

/// <summary>
/// HTTP-side knobs for the tenant middleware. Resolution-policy options live on
/// <see cref="Dragonfire.TenantContext.Resolution.TenantResolutionOptions"/>; this type controls
/// HTTP-specific behavior (status codes, header echoing).
/// </summary>
public sealed class TenantContextHttpOptions
{
    /// <summary>HTTP status code returned when resolution throws under <c>Throw</c> policy. Default: 400.</summary>
    public int FailureStatusCode { get; set; } = StatusCodes.Status400BadRequest;

    /// <summary>When set, the resolved tenant id is echoed back in this response header. Default: <c>null</c> (disabled).</summary>
    public string? ResponseHeader { get; set; }

    /// <summary>
    /// When <c>true</c>, the middleware short-circuits with <see cref="FailureStatusCode"/>
    /// instead of letting the exception bubble. Default: <c>false</c> (let exception handlers run).
    /// </summary>
    public bool WriteFailureResponse { get; set; }
}
