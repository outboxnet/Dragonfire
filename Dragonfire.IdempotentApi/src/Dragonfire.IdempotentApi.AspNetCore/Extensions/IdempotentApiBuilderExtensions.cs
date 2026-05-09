using Dragonfire.IdempotentApi.Builder;
using Dragonfire.IdempotentApi.Fingerprints;
using Dragonfire.IdempotentApi.Interfaces;
using Dragonfire.IdempotentApi.KeyReaders;
using Dragonfire.IdempotentApi.Policies;
using Dragonfire.IdempotentApi.Recording;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dragonfire.IdempotentApi.Extensions;

/// <summary>
/// Wires up the ASP.NET Core surface (key reader, fingerprint calculator, response
/// recorder, default policy). Each component is registered with <c>TryAdd</c> so callers
/// can pre-register custom replacements before this is called.
/// </summary>
public static class IdempotentApiBuilderExtensions
{
    /// <summary>
    /// Add the ASP.NET Core wiring. Pass <paramref name="configure"/> to opt into the
    /// attribute-based policy or supply other transport options.
    /// </summary>
    public static IIdempotentApiBuilder AddAspNetCore(
        this IIdempotentApiBuilder builder,
        Action<AspNetCoreIdempotencyOptions>? configure = null)
    {
        var aspOpts = new AspNetCoreIdempotencyOptions();
        configure?.Invoke(aspOpts);

        builder.Services.TryAddSingleton<IIdempotencyKeyReader, HeaderIdempotencyKeyReader>();
        builder.Services.TryAddSingleton<IRequestFingerprintCalculator, Sha256BodyFingerprintCalculator>();
        builder.Services.TryAddSingleton<IResponseRecorder, DefaultResponseRecorder>();

        if (aspOpts.UseAttributePolicy)
            builder.Services.TryAddSingleton<IIdempotencyPolicy, EndpointAttributeIdempotencyPolicy>();
        else
            builder.Services.TryAddSingleton<IIdempotencyPolicy, HttpMethodIdempotencyPolicy>();

        return builder;
    }
}

public sealed class AspNetCoreIdempotencyOptions
{
    /// <summary>
    /// When true the default policy is <see cref="EndpointAttributeIdempotencyPolicy"/>
    /// (only endpoints carrying <c>[Idempotent]</c> are processed).
    /// When false (default) the method-based policy is used.
    /// </summary>
    public bool UseAttributePolicy { get; set; }
}
