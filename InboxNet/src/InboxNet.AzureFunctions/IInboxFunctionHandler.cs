namespace InboxNet.AzureFunctions;

/// <summary>
/// Encapsulates the full receive and dispatch logic for Azure Functions.
/// Inject this into your function class and delegate to
/// <see cref="InboxFunctionBase.ReceiveAsync"/> / <see cref="InboxFunctionBase.DispatchAsync"/>.
/// </summary>
public interface IInboxFunctionHandler
{
    /// <summary>
    /// Validates the incoming webhook via the registered provider, persists it, and
    /// returns a result describing whether it was accepted, a duplicate, invalid, or unknown.
    /// </summary>
    Task<InboxReceiveResult> ReceiveAsync(
        string providerKey,
        string rawBody,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken ct = default);

    /// <summary>
    /// Runs one dispatch batch: locks pending inbox messages and runs their handlers.
    /// Call this from your timer trigger function.
    /// </summary>
    Task DispatchAsync(CancellationToken ct = default);
}
