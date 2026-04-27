using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using OutboxNet.Interfaces;
using OutboxNet.Models;
using OutboxNet.Observability;

namespace OutboxNet.Delivery;

internal sealed class HttpWebhookDeliverer : IWebhookDeliverer
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpWebhookDeliverer> _logger;

    public HttpWebhookDeliverer(HttpClient httpClient, ILogger<HttpWebhookDeliverer> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<DeliveryResult> DeliverAsync(
        OutboxMessage message,
        WebhookSubscription subscription,
        Guid? deliveryId = null,
        CancellationToken ct = default)
    {
        using var activity = OutboxActivitySource.Source.StartActivity("outbox.deliver_webhook");
        activity?.SetTag("outbox.message_id", message.Id.ToString());
        activity?.SetTag("outbox.subscription_id", subscription.Id.ToString());
        activity?.SetTag("outbox.event_type", message.EventType);
        activity?.SetTag("outbox.webhook_url", subscription.WebhookUrl);

        // Use the caller-supplied deliveryId if provided; otherwise fall back to a random one.
        // A deterministic deliveryId (derived from MessageId + SubscriptionId + AttemptNumber)
        // lets webhook consumers deduplicate retries of the same attempt using this header as
        // an idempotency key.
        var resolvedDeliveryId = deliveryId ?? Guid.NewGuid();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = HmacSignatureGenerator.ComputeSignature(message.Payload, subscription.Secret);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, subscription.WebhookUrl);
            request.Content = new StringContent(message.Payload, Encoding.UTF8);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            // Standard outbox headers
            request.Headers.Add("X-Outbox-Signature",       signature);
            request.Headers.Add("X-Outbox-Event",            message.EventType);
            request.Headers.Add("X-Outbox-Message-Id",       message.Id.ToString());
            request.Headers.Add("X-Outbox-Delivery-Id",      resolvedDeliveryId.ToString());
            request.Headers.Add("X-Outbox-Subscription-Id",  subscription.Id.ToString());
            request.Headers.Add("X-Outbox-Timestamp",        timestamp);

            if (message.CorrelationId is not null)
                request.Headers.Add("X-Outbox-Correlation-Id", message.CorrelationId);

            if (subscription.CustomHeaders is not null)
            {
                foreach (var header in subscription.CustomHeaders)
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(subscription.Timeout);

            using var response = await _httpClient.SendAsync(request, cts.Token);
            stopwatch.Stop();

            // Use the linked token so we stop reading if the per-subscription timeout fires.
            var responseBody = await response.Content.ReadAsStringAsync(cts.Token);
            if (responseBody.Length > 4000)
                responseBody = responseBody[..4000];

            var statusCode = (int)response.StatusCode;
            var success = response.IsSuccessStatusCode;

            OutboxMetrics.DeliveryAttempts.Add(1,  new KeyValuePair<string, object?>("event_type", message.EventType));
            OutboxMetrics.DeliveryDuration.Record(stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("event_type", message.EventType));

            if (success)
            {
                OutboxMetrics.DeliverySuccesses.Add(1, new KeyValuePair<string, object?>("event_type", message.EventType));
                _logger.LogDebug("Webhook delivered successfully to {Url} for message {MessageId} (HTTP {StatusCode})",
                    subscription.WebhookUrl, message.Id, statusCode);
            }
            else
            {
                OutboxMetrics.DeliveryFailures.Add(1, new KeyValuePair<string, object?>("event_type", message.EventType));
                _logger.LogWarning("Webhook delivery failed to {Url} for message {MessageId} (HTTP {StatusCode})",
                    subscription.WebhookUrl, message.Id, statusCode);
            }

            activity?.SetTag("http.status_code", statusCode);
            activity?.SetTag("outbox.delivery.success", success);

            return new DeliveryResult(success, statusCode, responseBody, success ? null : $"HTTP {statusCode}", stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            stopwatch.Stop();
            OutboxMetrics.DeliveryAttempts.Add(1,  new KeyValuePair<string, object?>("event_type", message.EventType));
            OutboxMetrics.DeliveryFailures.Add(1, new KeyValuePair<string, object?>("event_type", message.EventType));

            _logger.LogWarning("Webhook delivery timed out to {Url} for message {MessageId} after {Timeout}s",
                subscription.WebhookUrl, message.Id, subscription.Timeout.TotalSeconds);

            return new DeliveryResult(false, null, null, "Request timed out", stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            OutboxMetrics.DeliveryAttempts.Add(1,  new KeyValuePair<string, object?>("event_type", message.EventType));
            OutboxMetrics.DeliveryFailures.Add(1, new KeyValuePair<string, object?>("event_type", message.EventType));

            _logger.LogError(ex, "Webhook delivery failed to {Url} for message {MessageId}",
                subscription.WebhookUrl, message.Id);

            return new DeliveryResult(false, null, null, ex.Message, stopwatch.ElapsedMilliseconds);
        }
    }
}
