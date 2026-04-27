using FluentAssertions;
using Dragonfire.TenantContext.Serialization;
using Xunit;

namespace Dragonfire.TenantContext.Tests;

public class JsonTenantContextSerializerTests
{
    private readonly JsonTenantContextSerializer _sut = new();

    [Fact]
    public void Round_trip_preserves_id_source_and_properties()
    {
        var original = new TenantInfo(new TenantId("acme"), "header",
            new Dictionary<string, string> { ["region"] = "eu", ["tier"] = "gold" });

        var payload = _sut.Serialize(original);
        var roundTrip = _sut.Deserialize(payload);

        roundTrip.TenantId.Value.Should().Be("acme");
        roundTrip.Source.Should().Be("header");
        roundTrip.Properties.Should().ContainKey("region").WhoseValue.Should().Be("eu");
        roundTrip.Properties.Should().ContainKey("tier").WhoseValue.Should().Be("gold");
    }

    [Fact]
    public void Empty_tenant_serializes_to_empty_string()
    {
        _sut.Serialize(TenantInfo.None).Should().BeEmpty();
    }

    [Fact]
    public void Empty_or_null_payload_deserializes_to_None()
    {
        _sut.Deserialize(null).Should().BeSameAs(TenantInfo.None);
        _sut.Deserialize("").Should().BeSameAs(TenantInfo.None);
        _sut.Deserialize("   ").Should().BeSameAs(TenantInfo.None);
    }

    [Fact]
    public void Malformed_payload_throws_invalid_operation()
    {
        FluentActions.Invoking(() => _sut.Deserialize("{not json"))
            .Should().Throw<InvalidOperationException>();
    }
}
