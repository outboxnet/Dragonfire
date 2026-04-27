using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace InboxNet.LoadTests;

/// <summary>
/// Issues HMAC-signed webhook POSTs against the load test endpoint and records send timing
/// in the shared <see cref="LoadTestTracker"/>. Uses <see cref="Parallel.ForEachAsync"/> so
/// concurrency is bounded by <see cref="LoadTestConfig.PublisherConcurrency"/>.
/// </summary>
public sealed class LoadTestClient
{
    private readonly LoadTestConfig _config;
    private readonly LoadTestTracker _tracker;
    private readonly HttpClient _http;
    private readonly byte[] _signingKey;
    private readonly Uri _endpoint;

    public LoadTestClient(LoadTestConfig config, LoadTestTracker tracker)
    {
        _config = config;
        _tracker = tracker;
        _http = new HttpClient
        {
            // Account for retried POSTs queueing up under publisher concurrency burst.
            Timeout = TimeSpan.FromSeconds(30)
        };
        _signingKey = Encoding.UTF8.GetBytes(config.WebhookSecret);
        _endpoint = new Uri($"http://localhost:{config.ReceiverPort}/webhooks/loadtest");
    }

    public async Task<int> PublishAllAsync(CancellationToken ct)
    {
        var failures = 0;
        var ids = Enumerable.Range(0, _config.TotalMessages);

        await Parallel.ForEachAsync(
            ids,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _config.PublisherConcurrency,
                CancellationToken = ct
            },
            async (i, token) =>
            {
                var eventId = $"evt-{i:D8}-{Guid.NewGuid():N}";
                _tracker.RegisterExpected(eventId);

                var body = JsonSerializer.Serialize(new
                {
                    type = "loadtest.event",
                    sequence = i,
                    eventId,
                    payload = "x"
                });

                var signature = ComputeSignature(body);

                using var req = new HttpRequestMessage(HttpMethod.Post, _endpoint)
                {
                    Content = new StringContent(body, Encoding.UTF8)
                };
                req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                req.Headers.TryAddWithoutValidation("X-Signature-256", signature);
                req.Headers.TryAddWithoutValidation("X-Event-Id", eventId);

                _tracker.MarkSent(eventId);

                try
                {
                    using var resp = await _http.SendAsync(req, token);
                    if (!resp.IsSuccessStatusCode)
                    {
                        Interlocked.Increment(ref failures);
                    }
                }
                catch (Exception) when (!token.IsCancellationRequested)
                {
                    Interlocked.Increment(ref failures);
                }
            });

        return failures;
    }

    private string ComputeSignature(string body)
    {
        using var hmac = new HMACSHA256(_signingKey);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
