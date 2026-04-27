using System.Text.Json;
using InboxNet.Models;

namespace InboxNet.Extensions;

public static class InboxMessageExtensions
{
    /// <summary>
    /// Deserializes <see cref="InboxMessage.Payload"/> to <typeparamref name="T"/>. Returns
    /// <c>null</c> when the payload is the literal JSON <c>null</c> (mirroring
    /// <see cref="JsonSerializer.Deserialize{TValue}(string, JsonSerializerOptions?)"/>).
    /// Throws <see cref="JsonException"/> on malformed JSON — handlers should let that
    /// propagate so the message is retried / dead-lettered.
    /// </summary>
    public static T? PayloadAs<T>(this InboxMessage message, JsonSerializerOptions? options = null)
        => JsonSerializer.Deserialize<T>(message.Payload, options);
}
