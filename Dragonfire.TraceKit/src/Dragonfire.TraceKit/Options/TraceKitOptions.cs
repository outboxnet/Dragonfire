namespace Dragonfire.TraceKit.Options;

/// <summary>
/// Top-level options for <c>Dragonfire.TraceKit</c>. Bind from configuration or configure
/// in code via <see cref="Extensions.ServiceCollectionExtensions.AddTraceKit"/>.
/// </summary>
public sealed class TraceKitOptions
{
    /// <summary>Configuration section name conventionally used for binding.</summary>
    public const string SectionName = "TraceKit";

    /// <summary>Master switch. When false, the middleware and DelegatingHandler still run but emit nothing.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether to capture inbound API request/response bodies. Disable to record only
    /// metadata (method, URL, status, duration) at minimal CPU and memory cost.
    /// </summary>
    public bool CaptureInboundBodies { get; set; } = true;

    /// <summary>Whether to capture outbound third-party request/response bodies.</summary>
    public bool CaptureOutboundBodies { get; set; } = true;

    /// <summary>
    /// Maximum number of bytes of any single body to capture. Bodies longer than this are
    /// truncated and a <c>… [truncated N bytes]</c> marker is appended. Defaults to 64 KiB.
    /// </summary>
    public int MaxBodyBytes { get; set; } = 64 * 1024;

    /// <summary>
    /// Bounded capacity of the in-memory channel that decouples request capture from the
    /// repository write. When the channel is full, the oldest item is dropped so the
    /// request hot path is never blocked. Defaults to 10,000.
    /// </summary>
    public int ChannelCapacity { get; set; } = 10_000;

    /// <summary>
    /// MIME-type prefixes whose bodies are eligible for capture. Bodies of any other type
    /// (binary, video, etc.) are skipped — only their <c>Content-Type</c> and length are
    /// recorded. Defaults to common text and JSON types.
    /// </summary>
    public string[] CapturableContentTypePrefixes { get; set; } = new[]
    {
        "application/json",
        "application/xml",
        "application/x-www-form-urlencoded",
        "application/problem+json",
        "application/vnd.api+json",
        "text/",
    };

    /// <summary>
    /// Inbound request paths (case-insensitive prefix match) that should not be traced.
    /// Useful for health checks, swagger, and metrics endpoints.
    /// </summary>
    public string[] IgnoredPathPrefixes { get; set; } = new[]
    {
        "/health",
        "/healthz",
        "/metrics",
        "/swagger",
    };

    /// <summary>Redaction settings — which headers, JSON fields, and patterns to strip.</summary>
    public RedactionOptions Redaction { get; set; } = new();
}
