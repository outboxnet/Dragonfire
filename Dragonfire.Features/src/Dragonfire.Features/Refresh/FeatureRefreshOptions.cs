using System;

namespace Dragonfire.Features.Refresh;

/// <summary>
/// Tuning knobs for <see cref="FeatureRefreshHostedService"/>.
/// </summary>
public sealed class FeatureRefreshOptions
{
    /// <summary>
    /// How often the hosted service rebuilds the in-memory snapshot from registered
    /// <see cref="IFeatureSource"/>s. Default: 30 seconds.
    /// </summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// When <c>true</c> a transient failure (e.g. a flaky database) is logged and the previous
    /// snapshot is kept; when <c>false</c> the host is asked to crash. Default: <c>true</c>.
    /// </summary>
    public bool ContinueOnSourceError { get; set; } = true;

    /// <summary>
    /// When <c>true</c> the hosted service performs an initial load before <c>StartAsync</c>
    /// returns, so the application doesn't serve traffic with an empty store. Default: <c>true</c>.
    /// </summary>
    public bool LoadOnStartup { get; set; } = true;
}
