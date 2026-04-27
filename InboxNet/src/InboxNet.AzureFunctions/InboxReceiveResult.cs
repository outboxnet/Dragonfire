namespace InboxNet.AzureFunctions;

/// <summary>
/// Result returned by <see cref="IInboxFunctionHandler.ReceiveAsync"/> and mapped to
/// an HTTP response by <see cref="InboxFunctionBase"/>.
/// </summary>
public sealed record InboxReceiveResult(
    InboxReceiveStatus Status,
    Guid MessageId = default,
    string? InvalidReason = null);

public enum InboxReceiveStatus
{
    /// <summary>New message accepted and persisted. Maps to 202 Accepted.</summary>
    Accepted,

    /// <summary>Duplicate event — already persisted. Maps to 200 OK.</summary>
    Duplicate,

    /// <summary>Signature invalid or body malformed. Maps to 400 Bad Request.</summary>
    Invalid,

    /// <summary>No provider registered under that key. Maps to 404 Not Found.</summary>
    ProviderNotFound
}
