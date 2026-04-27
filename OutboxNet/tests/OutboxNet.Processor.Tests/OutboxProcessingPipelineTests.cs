using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OutboxNet.Interfaces;
using OutboxNet.Models;
using OutboxNet.Options;
using OutboxNet.Processor;
using Xunit;

namespace OutboxNet.Processor.Tests;

public class OutboxProcessingPipelineTests
{
    private readonly IOutboxStore _outboxStore = Substitute.For<IOutboxStore>();
    private readonly ISubscriptionReader _subscriptionReader = Substitute.For<ISubscriptionReader>();
    private readonly IDeliveryAttemptStore _deliveryAttemptStore = Substitute.For<IDeliveryAttemptStore>();
    private readonly IWebhookDeliverer _webhookDeliverer = Substitute.For<IWebhookDeliverer>();
    private readonly IRetryPolicy _retryPolicy = Substitute.For<IRetryPolicy>();

    private OutboxProcessingPipeline CreatePipeline(OutboxOptions? options = null)
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scope = Substitute.For<IServiceScope>();
        var sp = Substitute.For<IServiceProvider>();

        scope.ServiceProvider.Returns(sp);
        scopeFactory.CreateScope().Returns(scope);

        sp.GetService(typeof(IOutboxStore)).Returns(_outboxStore);
        sp.GetService(typeof(ISubscriptionReader)).Returns(_subscriptionReader);
        sp.GetService(typeof(IDeliveryAttemptStore)).Returns(_deliveryAttemptStore);
        sp.GetService(typeof(IWebhookDeliverer)).Returns(_webhookDeliverer);
        sp.GetService(typeof(IMessagePublisher)).Returns(null);

        var opts = Microsoft.Extensions.Options.Options.Create(options ?? new OutboxOptions());
        return new OutboxProcessingPipeline(
            scopeFactory,
            _retryPolicy,
            opts,
            NullLogger<OutboxProcessingPipeline>.Instance);
    }

    private void SetupMarkAsProcessed()
    {
        _outboxStore.MarkAsProcessedAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
    }

    private void SetupIncrementRetry()
    {
        _outboxStore.IncrementRetryAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(true);
    }

    private void SetupMarkAsDeadLettered()
    {
        _outboxStore.MarkAsDeadLetteredAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
    }

    private void SetupNoDeliveryStates()
    {
        _deliveryAttemptStore
            .GetDeliveryStatesAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, SubscriptionDeliveryState>());
    }

    private void SetupDeliveryState(Guid subscriptionId, int attemptCount, bool hasSuccess)
    {
        _deliveryAttemptStore
            .GetDeliveryStatesAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, SubscriptionDeliveryState>
            {
                [subscriptionId] = new SubscriptionDeliveryState(attemptCount, hasSuccess)
            });
    }

    [Fact]
    public async Task ProcessBatchAsync_ReleasesExpiredLocks_OnFirstPoll()
    {
        _outboxStore.LockNextBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<string>(), Arg.Any<IReadOnlySet<Guid>?>(), Arg.Any<CancellationToken>())
            .Returns(new List<OutboxMessage>());

        var pipeline = CreatePipeline();
        await pipeline.ProcessBatchAsync();

        // First call always triggers ReleaseExpiredLocksAsync (last-release starts at MinValue).
        await _outboxStore.Received(1).ReleaseExpiredLocksAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatchAsync_NoMessages_ReturnsZero()
    {
        _outboxStore.LockNextBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<string>(), Arg.Any<IReadOnlySet<Guid>?>(), Arg.Any<CancellationToken>())
            .Returns(new List<OutboxMessage>());

        var pipeline = CreatePipeline();
        var result = await pipeline.ProcessBatchAsync();

        result.Should().Be(0);
        await _subscriptionReader.DidNotReceive().GetForMessageAsync(Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatchAsync_WithMessage_NoSubscriptions_MarksAsProcessed()
    {
        SetupMarkAsProcessed();

        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = "order.placed",
            Payload = "{}",
            Status = MessageStatus.Processing,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _outboxStore.LockNextBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<string>(), Arg.Any<IReadOnlySet<Guid>?>(), Arg.Any<CancellationToken>())
            .Returns(new List<OutboxMessage> { message });

        _subscriptionReader.GetForMessageAsync(message, Arg.Any<CancellationToken>())
            .Returns(new List<WebhookSubscription>());

        var pipeline = CreatePipeline();
        await pipeline.ProcessBatchAsync();

        await _outboxStore.Received(1).MarkAsProcessedAsync(message.Id, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatchAsync_SuccessfulDelivery_MarksAsProcessed()
    {
        SetupMarkAsProcessed();
        SetupNoDeliveryStates();

        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = "order.placed",
            Payload = """{"id":1}""",
            Status = MessageStatus.Processing,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var subscription = new WebhookSubscription
        {
            Id = Guid.NewGuid(),
            EventType = "order.placed",
            WebhookUrl = "https://example.com/webhook",
            Secret = "secret",
            IsActive = true
        };

        _outboxStore.LockNextBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<string>(), Arg.Any<IReadOnlySet<Guid>?>(), Arg.Any<CancellationToken>())
            .Returns(new List<OutboxMessage> { message });

        _subscriptionReader.GetForMessageAsync(message, Arg.Any<CancellationToken>())
            .Returns(new List<WebhookSubscription> { subscription });

        _webhookDeliverer.DeliverAsync(message, subscription, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new DeliveryResult(true, 200, "OK", null, 50));

        var pipeline = CreatePipeline();
        await pipeline.ProcessBatchAsync();

        await _outboxStore.Received(1).MarkAsProcessedAsync(message.Id, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _deliveryAttemptStore.Received(1).SaveAttemptsAsync(
            Arg.Is<IReadOnlyList<DeliveryAttempt>>(list =>
                list.Count == 1 &&
                list[0].Status == DeliveryStatus.Success &&
                list[0].AttemptNumber == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatchAsync_FailedDelivery_WithRetriesRemaining_IncrementsRetry()
    {
        SetupIncrementRetry();
        SetupNoDeliveryStates();

        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = "order.placed",
            Payload = "{}",
            Status = MessageStatus.Processing,
            RetryCount = 0,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var subscription = new WebhookSubscription
        {
            Id = Guid.NewGuid(),
            EventType = "order.placed",
            WebhookUrl = "https://example.com/webhook",
            Secret = "secret",
            IsActive = true,
            MaxRetries = 5
        };

        _outboxStore.LockNextBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<string>(), Arg.Any<IReadOnlySet<Guid>?>(), Arg.Any<CancellationToken>())
            .Returns(new List<OutboxMessage> { message });

        _subscriptionReader.GetForMessageAsync(message, Arg.Any<CancellationToken>())
            .Returns(new List<WebhookSubscription> { subscription });

        _webhookDeliverer.DeliverAsync(message, subscription, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new DeliveryResult(false, 500, "Error", "HTTP 500", 100));

        _retryPolicy.GetNextDelay(0).Returns(TimeSpan.FromSeconds(10));

        var pipeline = CreatePipeline();
        await pipeline.ProcessBatchAsync();

        await _outboxStore.Received(1).IncrementRetryAsync(
            message.Id,
            Arg.Any<string>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatchAsync_FailedDelivery_RetriesExhausted_DeadLetters()
    {
        SetupMarkAsDeadLettered();

        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = "order.placed",
            Payload = "{}",
            Status = MessageStatus.Processing,
            RetryCount = 5,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var subscription = new WebhookSubscription
        {
            Id = Guid.NewGuid(),
            EventType = "order.placed",
            WebhookUrl = "https://example.com/webhook",
            Secret = "secret",
            IsActive = true,
            MaxRetries = 5
        };

        _outboxStore.LockNextBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<string>(), Arg.Any<IReadOnlySet<Guid>?>(), Arg.Any<CancellationToken>())
            .Returns(new List<OutboxMessage> { message });

        _subscriptionReader.GetForMessageAsync(message, Arg.Any<CancellationToken>())
            .Returns(new List<WebhookSubscription> { subscription });

        // Attempt count > MaxRetries and no prior success → exhausted without success → dead-letter.
        SetupDeliveryState(subscription.Id, 6, hasSuccess: false); // 6 > MaxRetries(5)

        var pipeline = CreatePipeline();
        await pipeline.ProcessBatchAsync();

        // All subscriptions exhausted without success → MarkAsDeadLettered
        await _outboxStore.Received(1).MarkAsDeadLetteredAsync(message.Id, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _outboxStore.DidNotReceive().MarkAsProcessedAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatchAsync_MultipleSubscriptions_AllMustSucceed()
    {
        SetupIncrementRetry();
        SetupNoDeliveryStates();

        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = "order.placed",
            Payload = "{}",
            Status = MessageStatus.Processing,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var sub1 = new WebhookSubscription { Id = Guid.NewGuid(), EventType = "order.placed", WebhookUrl = "https://a.com", Secret = "s1", IsActive = true, MaxRetries = 5 };
        var sub2 = new WebhookSubscription { Id = Guid.NewGuid(), EventType = "order.placed", WebhookUrl = "https://b.com", Secret = "s2", IsActive = true, MaxRetries = 5 };

        _outboxStore.LockNextBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<string>(), Arg.Any<IReadOnlySet<Guid>?>(), Arg.Any<CancellationToken>())
            .Returns(new List<OutboxMessage> { message });

        _subscriptionReader.GetForMessageAsync(message, Arg.Any<CancellationToken>())
            .Returns(new List<WebhookSubscription> { sub1, sub2 });

        // sub1 succeeds, sub2 fails
        _webhookDeliverer.DeliverAsync(message, sub1, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new DeliveryResult(true, 200, "OK", null, 50));
        _webhookDeliverer.DeliverAsync(message, sub2, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new DeliveryResult(false, 503, "Unavailable", "HTTP 503", 100));

        _retryPolicy.GetNextDelay(0).Returns(TimeSpan.FromSeconds(5));

        var pipeline = CreatePipeline();
        await pipeline.ProcessBatchAsync();

        // Should NOT mark as processed because sub2 failed
        await _outboxStore.DidNotReceive().MarkAsProcessedAsync(message.Id, Arg.Any<string>(), Arg.Any<CancellationToken>());
        // Should increment retry
        await _outboxStore.Received(1).IncrementRetryAsync(message.Id, Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatchAsync_AlreadySucceededSubscription_IsSkipped()
    {
        SetupMarkAsProcessed();

        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = "order.placed",
            Payload = "{}",
            Status = MessageStatus.Processing,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var subscription = new WebhookSubscription
        {
            Id = Guid.NewGuid(),
            EventType = "order.placed",
            WebhookUrl = "https://example.com/webhook",
            Secret = "secret",
            IsActive = true
        };

        _outboxStore.LockNextBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<string>(), Arg.Any<IReadOnlySet<Guid>?>(), Arg.Any<CancellationToken>())
            .Returns(new List<OutboxMessage> { message });

        _subscriptionReader.GetForMessageAsync(message, Arg.Any<CancellationToken>())
            .Returns(new List<WebhookSubscription> { subscription });

        // This subscription already succeeded previously — pipeline reads this via GetDeliveryStatesAsync.
        SetupDeliveryState(subscription.Id, 1, hasSuccess: true);

        var pipeline = CreatePipeline();
        await pipeline.ProcessBatchAsync();

        // Delivery should be skipped
        await _webhookDeliverer.DidNotReceive().DeliverAsync(Arg.Any<OutboxMessage>(), Arg.Any<WebhookSubscription>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        // All done (skipped), so mark processed
        await _outboxStore.Received(1).MarkAsProcessedAsync(message.Id, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatchAsync_BatchSaveAttempts_UsedInsteadOfIndividualSave()
    {
        SetupMarkAsProcessed();
        SetupNoDeliveryStates();

        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = "order.placed",
            Payload = "{}",
            Status = MessageStatus.Processing,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var sub1 = new WebhookSubscription { Id = Guid.NewGuid(), EventType = "order.placed", WebhookUrl = "https://a.com", Secret = "s1", IsActive = true };
        var sub2 = new WebhookSubscription { Id = Guid.NewGuid(), EventType = "order.placed", WebhookUrl = "https://b.com", Secret = "s2", IsActive = true };

        _outboxStore.LockNextBatchAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<string>(), Arg.Any<IReadOnlySet<Guid>?>(), Arg.Any<CancellationToken>())
            .Returns(new List<OutboxMessage> { message });

        _subscriptionReader.GetForMessageAsync(message, Arg.Any<CancellationToken>())
            .Returns(new List<WebhookSubscription> { sub1, sub2 });

        _webhookDeliverer.DeliverAsync(message, sub1, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new DeliveryResult(true, 200, "OK", null, 10));
        _webhookDeliverer.DeliverAsync(message, sub2, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new DeliveryResult(true, 200, "OK", null, 10));

        var pipeline = CreatePipeline();
        await pipeline.ProcessBatchAsync();

        // Both attempts saved in a single SaveAttemptsAsync call, not two SaveAttemptAsync calls.
        await _deliveryAttemptStore.Received(1).SaveAttemptsAsync(
            Arg.Is<IReadOnlyList<DeliveryAttempt>>(list => list.Count == 2),
            Arg.Any<CancellationToken>());
        await _deliveryAttemptStore.DidNotReceive().SaveAttemptAsync(Arg.Any<DeliveryAttempt>(), Arg.Any<CancellationToken>());
    }
}
