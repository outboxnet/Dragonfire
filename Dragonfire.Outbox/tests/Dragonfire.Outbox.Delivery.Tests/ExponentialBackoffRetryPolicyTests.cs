using FluentAssertions;
using Microsoft.Extensions.Options;
using Dragonfire.Outbox.Options;
using Xunit;

namespace Dragonfire.Outbox.Delivery.Tests;

public class ExponentialBackoffRetryPolicyTests
{
    private static ExponentialBackoffRetryPolicy CreatePolicy(
        int maxRetries = 5,
        double baseDelaySeconds = 5,
        double maxDelaySeconds = 300,
        double jitterFactor = 0)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new RetryPolicyOptions
        {
            MaxRetries = maxRetries,
            BaseDelay = TimeSpan.FromSeconds(baseDelaySeconds),
            MaxDelay = TimeSpan.FromSeconds(maxDelaySeconds),
            JitterFactor = jitterFactor
        });
        return new ExponentialBackoffRetryPolicy(options);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    public void ShouldRetry_RespectsMaxRetries(int retryCount, bool expected)
    {
        var policy = CreatePolicy(maxRetries: 5);

        policy.ShouldRetry(retryCount).Should().Be(expected);
    }

    [Fact]
    public void GetNextDelay_ReturnsNull_WhenRetriesExhausted()
    {
        var policy = CreatePolicy(maxRetries: 3);

        policy.GetNextDelay(3).Should().BeNull();
        policy.GetNextDelay(4).Should().BeNull();
    }

    [Fact]
    public void GetNextDelay_ExponentiallyIncreases_WithoutJitter()
    {
        var policy = CreatePolicy(baseDelaySeconds: 5, jitterFactor: 0);

        policy.GetNextDelay(0).Should().Be(TimeSpan.FromSeconds(5));   // 5 * 2^0
        policy.GetNextDelay(1).Should().Be(TimeSpan.FromSeconds(10));  // 5 * 2^1
        policy.GetNextDelay(2).Should().Be(TimeSpan.FromSeconds(20));  // 5 * 2^2
        policy.GetNextDelay(3).Should().Be(TimeSpan.FromSeconds(40));  // 5 * 2^3
        policy.GetNextDelay(4).Should().Be(TimeSpan.FromSeconds(80));  // 5 * 2^4
    }

    [Fact]
    public void GetNextDelay_CapsAtMaxDelay()
    {
        var policy = CreatePolicy(baseDelaySeconds: 5, maxDelaySeconds: 30, jitterFactor: 0);

        policy.GetNextDelay(0).Should().Be(TimeSpan.FromSeconds(5));
        policy.GetNextDelay(1).Should().Be(TimeSpan.FromSeconds(10));
        policy.GetNextDelay(2).Should().Be(TimeSpan.FromSeconds(20));
        policy.GetNextDelay(3).Should().Be(TimeSpan.FromSeconds(30)); // capped
        policy.GetNextDelay(4).Should().Be(TimeSpan.FromSeconds(30)); // capped
    }

    [Fact]
    public void GetNextDelay_WithJitter_StaysWithinBounds()
    {
        var policy = CreatePolicy(baseDelaySeconds: 10, jitterFactor: 0.2, maxDelaySeconds: 300);

        for (int i = 0; i < 100; i++)
        {
            var delay = policy.GetNextDelay(0);
            delay.Should().NotBeNull();
            // Base is 10s, jitter is +/- 20% = 8s to 12s
            delay!.Value.TotalSeconds.Should().BeInRange(0, 12);
        }
    }

    [Fact]
    public void GetNextDelay_FirstRetry_ReturnsBaseDelay_WithoutJitter()
    {
        var policy = CreatePolicy(baseDelaySeconds: 7, jitterFactor: 0);

        policy.GetNextDelay(0).Should().Be(TimeSpan.FromSeconds(7));
    }
}
