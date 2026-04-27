namespace InboxNet.Options;

public class InboxOptions
{
    /// <summary>Database schema for inbox tables. Default: "inbox".</summary>
    public string SchemaName { get; set; } = "inbox";

    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// How long a locked inbox message is invisible to other dispatcher instances.
    /// Must exceed worst-case per-message dispatch time (sum of all handler timeouts × sequential).
    /// Default: 10 minutes — leaves headroom over the default retry policy's 5-minute cap so
    /// a slow handler doesn't lose its lock mid-dispatch.
    /// </summary>
    public TimeSpan DefaultVisibilityTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Stable identity for this instance, written to the <c>LockedBy</c> column. Default
    /// resolution order: KUBERNETES_POD_NAME → WEBSITE_INSTANCE_ID → COMPUTERNAME →
    /// "{MachineName}-{ProcessId}". The default is deterministic per pod so post-mortem
    /// queries can correlate locks with infrastructure state.
    /// </summary>
    public string InstanceId { get; set; } = ResolveInstanceId();

    /// <summary>How many inbox messages are dispatched concurrently within a single batch. Default: 10.</summary>
    public int MaxConcurrentDispatch { get; set; } = 10;

    /// <summary>
    /// When true (default), messages with the same <c>(TenantId, ProviderKey, EntityId)</c>
    /// partition are dispatched strictly in receive order. A message is not locked until any
    /// in-flight message for the same partition has finished. Messages without an EntityId
    /// are unaffected and dispatched concurrently.
    /// </summary>
    public bool EnableOrderedProcessing { get; set; } = true;

    /// <summary>
    /// Optional tenant sharding. When set, only messages whose <c>TenantId</c> matches are
    /// dispatched by this instance. Null (default) = all tenants.
    /// </summary>
    public string? TenantFilter { get; set; }

    /// <summary>
    /// When true, the dispatcher batches per-message bookkeeping writes (mark-as-processed
    /// UPDATE plus handler-attempt INSERTs) into one round-trip per dispatch batch rather
    /// than per message. Trades a small increase in blast radius on a dispatcher crash —
    /// a whole batch's locks must expire before retry, instead of one message's — for a
    /// significant cut in per-message DB round-trips. Default: <c>true</c>.
    /// </summary>
    public bool BulkBookkeeping { get; set; } = true;

    /// <summary>
    /// When <c>false</c>, handler-attempt rows are written only on failure or dead-letter,
    /// not on success. Saves one INSERT per message on the happy path; the trade-off is
    /// loss of success-side forensics (timing, attempt count). Default: <c>true</c>.
    /// </summary>
    public bool RecordAttemptsOnSuccess { get; set; } = true;

    /// <summary>
    /// When <c>false</c>, the handler-attempt store is bypassed entirely — no prior-state
    /// SELECT and no INSERTs. The pipeline re-runs every registered handler on every
    /// dispatch, so handlers MUST be idempotent on <see cref="Models.InboxMessage.Id"/>.
    /// Use only when you have at most one handler per (provider, event) and that handler
    /// is naturally idempotent. Default: <c>true</c>.
    /// </summary>
    public bool RecordHandlerAttempts { get; set; } = true;

    /// <summary>
    /// Maximum allowed webhook request body size in bytes. Requests larger than this are
    /// rejected with HTTP 413. Default: 1 MB. Applies to <c>MapInboxWebhooks</c>; the Azure
    /// Functions host enforces its own limits.
    /// </summary>
    public long MaxBodyBytes { get; set; } = 1024 * 1024;

    /// <summary>
    /// When <c>true</c>, ContentSha256 is computed on every received webhook regardless of
    /// whether the provider supplied an event ID. When <c>false</c> (default), the SHA is
    /// only computed when no event ID is available — the dedup key falls back to the SHA
    /// in that case. Skips one allocation + hash for providers that always supply an event ID.
    /// </summary>
    public bool AlwaysComputeContentSha256 { get; set; } = false;

    private static string ResolveInstanceId()
    {
        var pod = Environment.GetEnvironmentVariable("KUBERNETES_POD_NAME");
        if (!string.IsNullOrWhiteSpace(pod)) return pod;

        var azureSlot = Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID");
        if (!string.IsNullOrWhiteSpace(azureSlot)) return azureSlot;

        var machine = Environment.MachineName;
        var pid = Environment.ProcessId;
        return $"{machine}-{pid}";
    }
}
