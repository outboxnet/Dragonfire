using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OutboxNet.Models;
using Xunit;

namespace OutboxNet.Delivery.Tests;

public class HttpWebhookDelivererTests
{
    private static OutboxMessage CreateMessage(string eventType = "order.placed", string payload = """{"id":1}""")
    {
        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = eventType,
            Payload = payload,
            CorrelationId = "corr-123",
            Status = MessageStatus.Processing,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static WebhookSubscription CreateSubscription(string url = "https://example.com/webhook")
    {
        return new WebhookSubscription
        {
            Id = Guid.NewGuid(),
            EventType = "order.placed",
            WebhookUrl = url,
            Secret = "test-secret",
            IsActive = true,
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    [Fact]
    public async Task DeliverAsync_SuccessfulDelivery_ReturnsSuccess()
    {
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("OK")
        });
        var httpClient = new HttpClient(handler);
        var deliverer = new HttpWebhookDeliverer(httpClient, NullLogger<HttpWebhookDeliverer>.Instance);

        var result = await deliverer.DeliverAsync(CreateMessage(), CreateSubscription());

        result.Success.Should().BeTrue();
        result.HttpStatusCode.Should().Be(200);
    }

    [Fact]
    public async Task DeliverAsync_ServerError_ReturnsFailure()
    {
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("Internal Server Error")
        });
        var httpClient = new HttpClient(handler);
        var deliverer = new HttpWebhookDeliverer(httpClient, NullLogger<HttpWebhookDeliverer>.Instance);

        var result = await deliverer.DeliverAsync(CreateMessage(), CreateSubscription());

        result.Success.Should().BeFalse();
        result.HttpStatusCode.Should().Be(500);
    }

    [Fact]
    public async Task DeliverAsync_SetsCorrectHeaders()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK),
            req => capturedRequest = req);
        var httpClient = new HttpClient(handler);
        var deliverer = new HttpWebhookDeliverer(httpClient, NullLogger<HttpWebhookDeliverer>.Instance);

        var message = CreateMessage();
        var subscription = CreateSubscription();

        await deliverer.DeliverAsync(message, subscription);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Headers.GetValues("X-Outbox-Signature").First().Should().StartWith("sha256=");
        capturedRequest.Headers.GetValues("X-Outbox-Event").First().Should().Be(message.EventType);
        capturedRequest.Headers.GetValues("X-Outbox-Delivery-Id").Should().ContainSingle();
        capturedRequest.Headers.GetValues("X-Outbox-Timestamp").Should().ContainSingle();
        capturedRequest.Headers.GetValues("X-Outbox-Correlation-Id").First().Should().Be("corr-123");
    }

    [Fact]
    public async Task DeliverAsync_SetsCustomHeaders()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK),
            req => capturedRequest = req);
        var httpClient = new HttpClient(handler);
        var deliverer = new HttpWebhookDeliverer(httpClient, NullLogger<HttpWebhookDeliverer>.Instance);

        var subscription = CreateSubscription();
        subscription.CustomHeaders = new Dictionary<string, string>
        {
            ["X-Custom-Header"] = "custom-value"
        };

        await deliverer.DeliverAsync(CreateMessage(), subscription);

        capturedRequest!.Headers.GetValues("X-Custom-Header").First().Should().Be("custom-value");
    }

    [Fact]
    public async Task DeliverAsync_NetworkError_ReturnsFailure()
    {
        var handler = new MockHttpMessageHandler(exception: new HttpRequestException("Connection refused"));
        var httpClient = new HttpClient(handler);
        var deliverer = new HttpWebhookDeliverer(httpClient, NullLogger<HttpWebhookDeliverer>.Instance);

        var result = await deliverer.DeliverAsync(CreateMessage(), CreateSubscription());

        result.Success.Should().BeFalse();
        result.HttpStatusCode.Should().BeNull();
        result.ErrorMessage.Should().Contain("Connection refused");
    }

    [Fact]
    public async Task DeliverAsync_TruncatesLongResponseBody()
    {
        var longBody = new string('x', 4000);
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(longBody)
        });
        var httpClient = new HttpClient(handler);
        var deliverer = new HttpWebhookDeliverer(httpClient, NullLogger<HttpWebhookDeliverer>.Instance);

        var result = await deliverer.DeliverAsync(CreateMessage(), CreateSubscription());

        result.ResponseBody!.Length.Should().Be(4000);
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage? _response;
        private readonly Exception? _exception;
        private readonly Action<HttpRequestMessage>? _onRequest;

        public MockHttpMessageHandler(HttpResponseMessage? response = null, Action<HttpRequestMessage>? onRequest = null, Exception? exception = null)
        {
            _response = response;
            _onRequest = onRequest;
            _exception = exception;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _onRequest?.Invoke(request);

            if (_exception is not null)
                throw _exception;

            return Task.FromResult(_response!);
        }
    }
}
