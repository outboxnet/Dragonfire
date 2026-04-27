using System.Collections.Concurrent;

namespace Dragonfire.TenantContext.Resolution;

/// <summary>
/// Transport-agnostic context handed to <see cref="ITenantResolver"/>s. Adapters (ASP.NET Core,
/// gRPC, message bus) populate <see cref="Properties"/> with whatever the resolver needs
/// (e.g. an <c>HttpContext</c>, request headers, a JWT principal). This keeps the core
/// independent of any web/transport stack.
/// </summary>
public sealed class TenantResolutionContext
{
    private readonly ConcurrentDictionary<string, object?> _properties = new(StringComparer.Ordinal);

    /// <summary>Well-known property key for an <c>HttpContext</c> (set by the ASP.NET Core adapter).</summary>
    public const string HttpContextKey = "http.context";

    /// <summary>Well-known property key for a gRPC <c>ServerCallContext</c>.</summary>
    public const string GrpcServerCallContextKey = "grpc.server.call";

    /// <summary>Well-known property key for a generic claims principal.</summary>
    public const string PrincipalKey = "auth.principal";

    /// <summary>Cancellation token associated with the originating operation.</summary>
    public CancellationToken CancellationToken { get; init; }

    /// <summary>
    /// Free-form properties bag. Resolvers should look up the typed values they care about;
    /// missing keys mean the resolver is not applicable.
    /// </summary>
    public IDictionary<string, object?> Properties => _properties;

    /// <summary>Strongly-typed accessor for a property; returns <c>default</c> when missing.</summary>
    public T? Get<T>(string key) where T : class
        => _properties.TryGetValue(key, out var v) ? v as T : null;

    /// <summary>Sets a property and returns the same context (fluent).</summary>
    public TenantResolutionContext With(string key, object? value)
    {
        _properties[key] = value;
        return this;
    }
}
