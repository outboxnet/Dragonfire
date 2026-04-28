using System;

namespace Dragonfire.Features;

/// <summary>
/// Declarative gate that ties a method, controller, endpoint, or service to a named feature flag.
/// The integration packages (AspNetCore, etc.) discover this attribute and short-circuit the
/// pipeline when <see cref="IFeatureResolver"/> reports the feature as disabled.
///
/// <code>
/// [FeatureGate("NewCheckout")]
/// public class CheckoutController : ControllerBase { ... }
///
/// app.MapPost("/orders", CreateOrder).RequireFeature("NewCheckout");
/// </code>
/// </summary>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Interface,
    AllowMultiple = true,
    Inherited = true)]
public sealed class FeatureGateAttribute : Attribute
{
    public FeatureGateAttribute(string featureName)
    {
        if (string.IsNullOrWhiteSpace(featureName))
            throw new ArgumentException("Feature name must be non-empty.", nameof(featureName));

        FeatureName = featureName;
    }

    /// <summary>The feature flag this gate evaluates.</summary>
    public string FeatureName { get; }

    /// <summary>
    /// HTTP status code returned by AspNetCore integrations when the gate denies access.
    /// Defaults to 404 to avoid leaking the existence of in-development features.
    /// </summary>
    public int DeniedStatusCode { get; set; } = 404;
}
