using Dragonfire.IdempotentApi.Interfaces;
using Dragonfire.IdempotentApi.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Dragonfire.IdempotentApi.Recording;

/// <summary>
/// Default recorder. Expects the middleware to have wrapped <see cref="HttpResponse.Body"/>
/// with a <see cref="MemoryStream"/> before invoking the next pipeline step — that buffer
/// is what gets snapshotted into the store.
/// </summary>
public sealed class DefaultResponseRecorder : IResponseRecorder
{
    public Task<IdempotentResponse> CaptureAsync(HttpContext context, CancellationToken ct)
    {
        var response = context.Response;
        if (response.Body is not MemoryStream buffer)
            throw new InvalidOperationException(
                "DefaultResponseRecorder requires the response body to be a MemoryStream. " +
                "Use the Dragonfire.IdempotentApi middleware (or wrap the body manually).");

        buffer.Position = 0;
        var bytes = buffer.ToArray();

        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in response.Headers)
            headers[h.Key] = h.Value.ToArray()!;

        return Task.FromResult(new IdempotentResponse
        {
            StatusCode = response.StatusCode,
            ContentType = response.ContentType,
            Body = bytes,
            Headers = headers,
        });
    }

    public async Task ReplayAsync(HttpContext context, IdempotentResponse stored, CancellationToken ct)
    {
        var response = context.Response;
        response.StatusCode = stored.StatusCode;
        if (stored.ContentType is not null)
            response.ContentType = stored.ContentType;

        foreach (var h in stored.Headers)
        {
            // Skip Content-Length — ASP.NET Core derives it from the body we'll write next.
            if (string.Equals(h.Key, HeaderNames.ContentLength, StringComparison.OrdinalIgnoreCase))
                continue;

            response.Headers[h.Key] = h.Value;
        }

        // Marker so callers can tell a replay from an original response.
        response.Headers["Idempotent-Replay"] = "true";

        if (stored.Body.Length > 0)
            await response.Body.WriteAsync(stored.Body, ct);
    }
}
