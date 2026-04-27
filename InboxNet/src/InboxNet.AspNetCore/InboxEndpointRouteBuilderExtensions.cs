using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using InboxNet.Interfaces;
using InboxNet.Models;
using InboxNet.Observability;
using InboxNet.Options;

namespace InboxNet.AspNetCore;

public static class InboxEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps a POST endpoint that receives webhooks for a single provider. The raw request
    /// body is buffered once (capped by <see cref="InboxOptions.MaxBodyBytes"/>), handed to
    /// the provider's <see cref="IWebhookProvider.ParseAsync"/> for validation, then
    /// persisted via <see cref="IInboxPublisher"/>.
    /// <para>
    /// Response codes:
    /// <list type="bullet">
    /// <item><description><c>202 Accepted</c> — new message accepted</description></item>
    /// <item><description><c>200 OK</c> — duplicate of an already-received message (idempotent replay)</description></item>
    /// <item><description><c>400 Bad Request</c> — provider rejected the request (bad signature, malformed body, etc.)</description></item>
    /// <item><description><c>404 Not Found</c> — no provider is registered under that key</description></item>
    /// <item><description><c>413 Payload Too Large</c> — body exceeded <see cref="InboxOptions.MaxBodyBytes"/></description></item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">Route pattern. Must contain a <c>{providerKey}</c> segment. Defaults to <c>/webhooks/{providerKey}</c>.</param>
    public static RouteHandlerBuilder MapInboxWebhooks(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/webhooks/{providerKey}")
    {
        return endpoints.MapPost(pattern,
            (string providerKey, HttpContext context) => HandleAsync(context, providerKey));
    }

    /// <summary>
    /// Maps a POST endpoint bound to a single provider key. Useful when the provider key
    /// isn't a route segment (e.g. one hard-coded endpoint per provider).
    /// </summary>
    public static RouteHandlerBuilder MapInboxWebhook(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        string providerKey)
    {
        // Cast to Delegate to pick the MapPost(Delegate) overload rather than
        // MapPost(RequestDelegate), which returns IEndpointConventionBuilder.
        return endpoints.MapPost(pattern,
            (Delegate)((HttpContext context) => HandleAsync(context, providerKey)));
    }

    private static async Task<IResult> HandleAsync(HttpContext context, string providerKey)
    {
        var sp = context.RequestServices;
        var registry = sp.GetRequiredService<IWebhookProviderRegistry>();
        var publisher = sp.GetRequiredService<IInboxPublisher>();
        var inboxOptions = sp.GetRequiredService<IOptions<InboxOptions>>().Value;
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("InboxNet.Webhook");

        var provider = registry.Get(providerKey);
        if (provider is null)
        {
            logger.LogWarning("No webhook provider registered for key {ProviderKey}", providerKey);
            return Results.NotFound(new { error = $"Unknown provider '{providerKey}'" });
        }

        using var activity = InboxActivitySource.Source.StartActivity("inbox.receive");
        activity?.SetTag("inbox.provider_key", providerKey);

        // Reject early when Content-Length is present and over the limit — saves the
        // upload of a payload we'd refuse anyway.
        var maxBodyBytes = inboxOptions.MaxBodyBytes;
        if (context.Request.ContentLength is long declaredLen && declaredLen > maxBodyBytes)
        {
            logger.LogWarning(
                "Webhook for provider {ProviderKey} declared body length {Length} > limit {Limit}",
                providerKey, declaredLen, maxBodyBytes);
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        // Buffer the body once with an enforced upper bound. Providers need the raw bytes
        // for signature verification; re-reading request.Body is not safe.
        string rawBody;
        try
        {
            // bufferThreshold = bytes kept in memory before spooling to disk.
            // bufferLimit = absolute maximum; reads above this throw.
            var bufferThreshold = (int)Math.Min(maxBodyBytes, 64 * 1024);
            context.Request.EnableBuffering(bufferThreshold, maxBodyBytes);
            rawBody = await ReadBoundedAsync(context.Request.Body, maxBodyBytes, context.RequestAborted);
            context.Request.Body.Position = 0;
        }
        catch (BodyTooLargeException)
        {
            logger.LogWarning(
                "Webhook for provider {ProviderKey} exceeded body size limit {Limit} during read",
                providerKey, maxBodyBytes);
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to buffer webhook body for provider {ProviderKey}", providerKey);
            return Results.BadRequest(new { error = "Failed to read request body" });
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in context.Request.Headers)
        {
            if (h.Value.Count > 0)
                headers[h.Key] = h.Value.ToString();
        }

        var webhookContext = new WebhookRequestContext
        {
            ProviderKey = providerKey,
            RawBody = rawBody,
            Headers = headers,
            RoutePath = context.Request.Path.HasValue ? context.Request.Path.Value : null,
            QueryString = context.Request.QueryString.HasValue ? context.Request.QueryString.Value : null
        };

        var parse = await provider.ParseAsync(webhookContext, context.RequestAborted);

        if (!parse.IsValid)
        {
            InboxMetrics.MessagesInvalid.Add(1,
                new KeyValuePair<string, object?>("provider", providerKey));

            logger.LogWarning(
                "Webhook rejected by provider {ProviderKey}: {Reason}",
                providerKey, parse.InvalidReason);

            activity?.SetTag("inbox.valid", false);
            activity?.SetTag("inbox.invalid_reason", parse.InvalidReason);

            return Results.BadRequest(new { error = parse.InvalidReason ?? "Invalid webhook" });
        }

        var result = await publisher.PublishAsync(providerKey, parse, context.RequestAborted);

        if (result.IsDuplicate)
        {
            // 200 rather than 202 so providers that distinguish them see idempotent replay.
            return Results.Ok(new { messageId = result.MessageId, duplicate = true });
        }

        return Results.Accepted(value: new { messageId = result.MessageId });
    }

    /// <summary>
    /// Reads the stream as UTF-8 text with a size limit.
    /// </summary>
    /// <param name="body">Stream to read from (not disposed)</param>
    /// <param name="maxBytes">Maximum allowed bytes</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Decoded string</returns>
    /// <exception cref="BodyTooLargeException">
    /// Thrown when stream exceeds maxBytes
    /// </exception>
    private static async Task<string> ReadBoundedAsync(
        Stream body,
        long maxBytes,
        CancellationToken ct)
    {
        if (maxBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));

        // Use MemoryStream as a safe accumulator for small/medium payloads
        // This is surprisingly efficient for typical webhooks (<100KB)
        using var memoryStream = new MemoryStream();

        const int bufferSize = 81920; // 80KB - optimal for network streams
        byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(bufferSize);

        try
        {
            long totalBytes = 0;
            int bytesRead;

            while ((bytesRead = await body.ReadAsync(
                buffer.AsMemory(0, bufferSize), ct).ConfigureAwait(false)) > 0)
            {
                totalBytes += bytesRead;

                if (totalBytes > maxBytes)
                {
                    throw new BodyTooLargeException(
                        $"Request body exceeds {maxBytes} bytes. " +
                        $"Read {totalBytes} bytes before aborting.");
                }

                await memoryStream.WriteAsync(
                    buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
            }

            // Seek to beginning for reading
            memoryStream.Position = 0;

            // Now decode everything at once - correctly handles UTF-8
            using var reader = new StreamReader(
                memoryStream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 4096,
                leaveOpen: false);

            return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private sealed class BodyTooLargeException : Exception 
    {
        public BodyTooLargeException(string message): base(message) { }
    }
}
