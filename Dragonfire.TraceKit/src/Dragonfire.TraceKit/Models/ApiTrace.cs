using System.Collections.Generic;

namespace Dragonfire.TraceKit.Models;

/// <summary>
/// A single captured HTTP exchange — either the inbound API request handled by this service
/// or an outbound third-party HttpClient call performed while serving it. Library consumers
/// receive this DTO via <see cref="Abstractions.ITraceRepository"/> and persist it however
/// they like; the library imposes no entity, ORM, or schema.
/// </summary>
public sealed class ApiTrace
{
    /// <summary>Unique id for this individual exchange.</summary>
    public Guid TraceId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Correlation id shared by the inbound call and every outbound third-party call made
    /// while serving it. Use this to group all rows belonging to one API request.
    /// </summary>
    public string CorrelationId { get; set; } = string.Empty;

    /// <summary>
    /// Monotonically increasing sequence number assigned within a single inbound request.
    /// The inbound request itself is <c>0</c>; outbound calls start at <c>1</c>.
    /// Generated via <see cref="System.Threading.Interlocked"/> so it is correct under
    /// parallel third-party calls.
    /// </summary>
    public int Sequence { get; set; }

    /// <summary>Inbound vs. outbound third-party.</summary>
    public TraceKind Kind { get; set; }

    /// <summary>HTTP method (GET, POST, …).</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>Full request URL including query string.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Logical name for this exchange. For outbound calls, the HttpClient name; for inbound, the route template (when available).</summary>
    public string? OperationName { get; set; }

    /// <summary>HTTP status code of the response, or <c>null</c> if the exchange threw before producing a response.</summary>
    public int? StatusCode { get; set; }

    /// <summary>Wall-clock UTC time the exchange started.</summary>
    public DateTimeOffset StartedAtUtc { get; set; }

    /// <summary>Wall-clock UTC time the exchange completed (success or failure).</summary>
    public DateTimeOffset CompletedAtUtc { get; set; }

    /// <summary>Total elapsed time of the exchange.</summary>
    public TimeSpan Duration => CompletedAtUtc - StartedAtUtc;

    /// <summary>Request headers, post-redaction.</summary>
    public IDictionary<string, string> RequestHeaders { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Response headers, post-redaction.</summary>
    public IDictionary<string, string> ResponseHeaders { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Request body, post-redaction. May be truncated according to options.</summary>
    public string? RequestBody { get; set; }

    /// <summary>Response body, post-redaction. May be truncated according to options.</summary>
    public string? ResponseBody { get; set; }

    /// <summary>Request <c>Content-Type</c>, if any.</summary>
    public string? RequestContentType { get; set; }

    /// <summary>Response <c>Content-Type</c>, if any.</summary>
    public string? ResponseContentType { get; set; }

    /// <summary>Exception type name when the exchange threw, otherwise <c>null</c>.</summary>
    public string? ExceptionType { get; set; }

    /// <summary>Exception message when the exchange threw, otherwise <c>null</c>.</summary>
    public string? ExceptionMessage { get; set; }

    /// <summary>Tenant identifier resolved from <c>HttpContext</c> or claims (optional, set by host).</summary>
    public string? TenantId { get; set; }

    /// <summary>User identifier resolved from <c>HttpContext</c> or claims (optional, set by host).</summary>
    public string? UserId { get; set; }

    /// <summary>Free-form tags the host can attach (route data, feature flags, etc.).</summary>
    public IDictionary<string, string?> Tags { get; set; } = new Dictionary<string, string?>(StringComparer.Ordinal);
}
