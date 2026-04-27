using FluentAssertions;
using OutboxNet.Serialization;
using Xunit;

namespace OutboxNet.Core.Tests;

public class JsonMessageSerializerTests
{
    private readonly JsonMessageSerializer _serializer = new();

    [Fact]
    public void Serialize_ReturnsValidJson()
    {
        var payload = new { OrderId = 123, Total = 99.99m };

        var json = _serializer.Serialize(payload);

        json.Should().Contain("\"orderId\"");
        json.Should().Contain("123");
    }

    [Fact]
    public void Serialize_UsesCamelCase()
    {
        var payload = new { MyProperty = "value" };

        var json = _serializer.Serialize(payload);

        json.Should().Contain("\"myProperty\"");
        json.Should().NotContain("\"MyProperty\"");
    }

    [Fact]
    public void Deserialize_ReturnsCorrectObject()
    {
        var json = """{"name":"test","value":42}""";

        var result = _serializer.Deserialize<TestPayload>(json);

        result.Should().NotBeNull();
        result!.Name.Should().Be("test");
        result.Value.Should().Be(42);
    }

    [Fact]
    public void RoundTrip_PreservesData()
    {
        var original = new TestPayload { Name = "test", Value = 42 };

        var json = _serializer.Serialize(original);
        var deserialized = _serializer.Deserialize<TestPayload>(json);

        deserialized.Should().NotBeNull();
        deserialized!.Name.Should().Be(original.Name);
        deserialized.Value.Should().Be(original.Value);
    }

    private class TestPayload
    {
        public string Name { get; set; } = default!;
        public int Value { get; set; }
    }
}
