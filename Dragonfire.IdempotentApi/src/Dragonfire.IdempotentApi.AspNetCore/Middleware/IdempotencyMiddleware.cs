using Dragonfire.IdempotentApi.Attributes;
using Dragonfire.IdempotentApi.Interfaces;
using Dragonfire.IdempotentApi.Models;
using Dragonfire.IdempotentApi.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dragonfire.IdempotentApi.Middleware;

/// <summary>
/// The pipeline component. For requests matched by <see cref="IIdempotencyPolicy"/>:
/// reads the key, fingerprints the body, asks the store for a reservation, and either
/// runs the handler (capturing the response on success) or replays a stored response.
/// </summary>
public sealed class IdempotencyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IIdempotencyPolicy _policy;
    private readonly IIdempotencyKeyReader _keyReader;
    private readonly IRequestFingerprintCalculator _fingerprint;
    private readonly IResponseRecorder _recorder;
    private readonly IIdempotencyStore _store;
    private readonly IdempotentApiOptions _options;
    private readonly ILogger<IdempotencyMiddleware> _logger;

    public IdempotencyMiddleware(
        RequestDelegate next,
        IIdempotencyPolicy policy,
        IIdempotencyKeyReader keyReader,
        IRequestFingerprintCalculator fingerprint,
        IResponseRecorder recorder,
        IIdempotencyStore store,
        IOptions<IdempotentApiOptions> options,
        ILogger<IdempotencyMiddleware> logger)
    {
        _next = next;
        _policy = policy;
        _keyReader = keyReader;
        _fingerprint = fingerprint;
        _recorder = recorder;
        _store = store;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_policy.ShouldHandle(context))
        {
            await _next(context);
            return;
        }

        if (!_keyReader.TryRead(context, out var key) || string.IsNullOrWhiteSpace(key))
        {
            if (_options.MissingKeyBehavior == MissingKeyBehavior.RequireKey)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync(
                    $"Missing required {_options.HeaderName} header.",
                    context.RequestAborted);
                return;
            }

            await _next(context);
            return;
        }

        var ct = context.RequestAborted;

        // Fingerprint the request — body buffering is the calculator's responsibility.
        var fingerprint = await _fingerprint.CalculateAsync(context, ct);

        // Endpoint may override the expiration via [Idempotent(Expiration = ...)].
        var endpointAttr = context.GetEndpoint()?.Metadata.GetMetadata<IdempotentAttribute>();
        var expiration = endpointAttr?.Expiration ?? _options.DefaultExpiration;
        var expiresAt = DateTimeOffset.UtcNow.Add(expiration);

        var outcome = await _store.TryReserveAsync(key!, fingerprint, expiresAt, ct);

        switch (outcome.Kind)
        {
            case ReservationKind.AlreadyCompleted when outcome.Entry?.Response is not null:
                _logger.LogDebug("Replaying stored response for idempotency key {Key}", key);
                await _recorder.ReplayAsync(context, outcome.Entry.Response, ct);
                return;

            case ReservationKind.InProgress:
                _logger.LogDebug("Duplicate in-flight request for idempotency key {Key}", key);
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                await context.Response.WriteAsync(
                    $"A request with idempotency key {key} is already in progress.", ct);
                return;

            case ReservationKind.FingerprintMismatch:
                _logger.LogWarning("Fingerprint mismatch for idempotency key {Key}", key);
                context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
                await context.Response.WriteAsync(
                    $"Idempotency key {key} was previously used with a different request payload.", ct);
                return;
        }

        // Acquired — buffer response so we can capture it.
        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await _next(context);

            var captured = await _recorder.CaptureAsync(context, ct);
            await _store.SaveResponseAsync(key!, captured, ct);

            buffer.Position = 0;
            await buffer.CopyToAsync(originalBody, ct);
        }
        catch
        {
            // Roll the reservation back so a retry from the client can succeed.
            try { await _store.ReleaseReservationAsync(key!, CancellationToken.None); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to release reservation for {Key} after handler exception", key);
            }
            throw;
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }
}
