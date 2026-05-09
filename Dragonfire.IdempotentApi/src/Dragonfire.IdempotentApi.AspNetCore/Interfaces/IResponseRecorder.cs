using Dragonfire.IdempotentApi.Models;
using Microsoft.AspNetCore.Http;

namespace Dragonfire.IdempotentApi.Interfaces;

/// <summary>
/// Captures an outgoing response into a storage-friendly DTO and replays a stored
/// DTO onto an outgoing response.
/// </summary>
public interface IResponseRecorder
{
    Task<IdempotentResponse> CaptureAsync(HttpContext context, CancellationToken ct);
    Task ReplayAsync(HttpContext context, IdempotentResponse stored, CancellationToken ct);
}
