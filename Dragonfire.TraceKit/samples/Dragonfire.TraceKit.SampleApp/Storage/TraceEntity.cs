namespace Dragonfire.TraceKit.SampleApp.Storage;

/// <summary>
/// Persistence entity owned by the SampleApp — NOT by the TraceKit library. The library
/// hands us a transport DTO (<see cref="Dragonfire.TraceKit.Models.ApiTrace"/>) and we
/// translate it into whichever shape suits the host. Headers and tags are flattened to
/// JSON strings to keep the schema small.
/// </summary>
public sealed class TraceEntity
{
    public Guid TraceId { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public int Sequence { get; set; }

    /// <summary>0 = inbound, 1 = outbound (mirrors <see cref="Dragonfire.TraceKit.Models.TraceKind"/>).</summary>
    public byte Kind { get; set; }

    public string Method { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? OperationName { get; set; }
    public int? StatusCode { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset CompletedAtUtc { get; set; }
    public int DurationMs { get; set; }

    public string? RequestContentType { get; set; }
    public string? ResponseContentType { get; set; }

    /// <summary>JSON-encoded headers (post-redaction). Stored as <c>NVARCHAR(MAX)</c>.</summary>
    public string RequestHeadersJson { get; set; } = "{}";

    /// <summary>JSON-encoded headers (post-redaction). Stored as <c>NVARCHAR(MAX)</c>.</summary>
    public string ResponseHeadersJson { get; set; } = "{}";

    public string? RequestBody { get; set; }
    public string? ResponseBody { get; set; }

    public string? ExceptionType { get; set; }
    public string? ExceptionMessage { get; set; }

    public string? TenantId { get; set; }
    public string? UserId { get; set; }

    public string? TagsJson { get; set; }
}
