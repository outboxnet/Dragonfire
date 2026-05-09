namespace Dragonfire.IdempotentApi.Options;

/// <summary>
/// Top-level configuration shared by every layer (middleware, key readers, store).
/// </summary>
public sealed class IdempotentApiOptions
{
    /// <summary>HTTP header carrying the idempotency key. Default: <c>Idempotency-Key</c>.</summary>
    public string HeaderName { get; set; } = "Idempotency-Key";

    /// <summary>How long completed entries live before they can be reclaimed. Default: 24 h.</summary>
    public TimeSpan DefaultExpiration { get; set; } = TimeSpan.FromHours(24);

    /// <summary>HTTP methods the default policy treats as idempotency-managed.</summary>
    public string[] Methods { get; set; } = ["POST", "PUT", "PATCH"];

    /// <summary>Maximum request body size that will be buffered + fingerprinted. Default: 1 MiB.</summary>
    public long MaxBodyBytes { get; set; } = 1L * 1024 * 1024;

    /// <summary>What to do when a request matches the policy but no key header is present.</summary>
    public MissingKeyBehavior MissingKeyBehavior { get; set; } = MissingKeyBehavior.Bypass;
}

/// <summary>Behavior when the policy matches but no idempotency-key header is present.</summary>
public enum MissingKeyBehavior
{
    /// <summary>Pass the request through unchanged. (Useful for gradual rollout.)</summary>
    Bypass,

    /// <summary>Reject the request with HTTP 400. (Strict mode.)</summary>
    RequireKey,
}
