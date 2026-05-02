namespace Dragonfire.TraceKit.Models;

/// <summary>
/// Distinguishes the inbound API call from outbound third-party HTTP calls performed
/// while serving that inbound call.
/// </summary>
public enum TraceKind
{
    /// <summary>The inbound request received by this ASP.NET Core API.</summary>
    Inbound = 0,

    /// <summary>An outbound HttpClient call made while handling the inbound request.</summary>
    OutboundThirdParty = 1,
}
