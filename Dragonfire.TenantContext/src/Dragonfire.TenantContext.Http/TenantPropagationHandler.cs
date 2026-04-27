using Microsoft.Extensions.Options;

namespace Dragonfire.TenantContext.Http;

/// <summary>Options for <see cref="TenantPropagationHandler"/>.</summary>
public sealed class TenantPropagationOptions
{
    /// <summary>Outbound header name. Default: <c>X-Tenant-Id</c>.</summary>
    public string HeaderName { get; set; } = "X-Tenant-Id";

    /// <summary>
    /// What to do when no tenant is present in the ambient context.
    /// Default: <see cref="MissingBehavior.Skip"/> (don't add the header).
    /// </summary>
    public MissingBehavior OnMissing { get; set; } = MissingBehavior.Skip;

    /// <summary>When <c>true</c>, an existing header on the request is preserved. Default: <c>false</c> (overwrite).</summary>
    public bool PreserveExistingHeader { get; set; }
}

/// <summary>Behavior when there is no ambient tenant.</summary>
public enum MissingBehavior
{
    /// <summary>Send the request without the tenant header.</summary>
    Skip = 0,
    /// <summary>Throw <see cref="InvalidOperationException"/>.</summary>
    Throw = 1,
}

/// <summary>
/// <see cref="DelegatingHandler"/> that stamps the current tenant id onto outgoing HTTP requests.
/// Register against any typed/named <see cref="HttpClient"/>; safe to compose with other handlers
/// (Polly retries, logging, auth) — it sets the header on every attempt because the handler runs
/// per-request, not per-attempt-of-Polly outer policy.
/// </summary>
public sealed class TenantPropagationHandler : DelegatingHandler
{
    private readonly ITenantContextAccessor _accessor;
    private readonly TenantPropagationOptions _options;

    public TenantPropagationHandler(ITenantContextAccessor accessor, IOptions<TenantPropagationOptions> options)
    {
        _accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value ?? new TenantPropagationOptions();
        if (string.IsNullOrWhiteSpace(_options.HeaderName))
            throw new InvalidOperationException("TenantPropagationHandler: HeaderName is required.");
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tenant = _accessor.Current;
        if (!tenant.IsResolved)
        {
            if (_options.OnMissing == MissingBehavior.Throw)
                throw new InvalidOperationException("No ambient tenant; cannot propagate to outbound HTTP request.");
        }
        else
        {
            var hasExisting = request.Headers.Contains(_options.HeaderName);
            if (!hasExisting || !_options.PreserveExistingHeader)
            {
                if (hasExisting) request.Headers.Remove(_options.HeaderName);
                request.Headers.TryAddWithoutValidation(_options.HeaderName, tenant.TenantId.Value);
            }
        }

        return base.SendAsync(request, cancellationToken);
    }
}
