using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Dragonfire.Outbox.Extensions;
using Dragonfire.Outbox.Interfaces;
using Dragonfire.Outbox.Options;
using Xunit;

namespace Dragonfire.Outbox.Core.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddOutboxNet_RegistersOptions()
    {
        var services = new ServiceCollection();

        services.AddOutboxNet(o =>
        {
            o.BatchSize = 100;
            o.SchemaName = "custom";
            o.InstanceId = "test-instance";
        });

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OutboxOptions>>();

        options.Value.BatchSize.Should().Be(100);
        options.Value.SchemaName.Should().Be("custom");
        options.Value.InstanceId.Should().Be("test-instance");
    }

    [Fact]
    public void AddOutboxNet_RegistersSerializer()
    {
        var services = new ServiceCollection();

        services.AddOutboxNet();

        var provider = services.BuildServiceProvider();
        var serializer = provider.GetRequiredService<IMessageSerializer>();

        serializer.Should().NotBeNull();
    }

    [Fact]
    public void AddOutboxNet_ReturnsBuilder()
    {
        var services = new ServiceCollection();

        var builder = services.AddOutboxNet();

        builder.Should().NotBeNull();
        builder.Services.Should().BeSameAs(services);
    }

    [Fact]
    public void AddOutboxNet_DefaultOptions_HaveSensibleDefaults()
    {
        var services = new ServiceCollection();

        services.AddOutboxNet();

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OutboxOptions>>();

        options.Value.BatchSize.Should().Be(50);
        options.Value.SchemaName.Should().Be("outbox");
        options.Value.DefaultVisibilityTimeout.Should().Be(TimeSpan.FromMinutes(5));
        options.Value.MaxConcurrentDeliveries.Should().Be(10);
        options.Value.ProcessingMode.Should().Be(ProcessingMode.DirectDelivery);
    }
}
