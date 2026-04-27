using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using OutboxNet.Delivery;
using Xunit;

namespace OutboxNet.Delivery.Tests;

public class HmacSignatureGeneratorTests
{
    [Fact]
    public void ComputeSignature_ReturnsCorrectHmacSha256()
    {
        var payload = """{"orderId":"123","total":99.99}""";
        var secret = "my-webhook-secret";

        var result = HmacSignatureGenerator.ComputeSignature(payload, secret);

        // Verify independently
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var expectedHash = HMACSHA256.HashData(keyBytes, payloadBytes);
        var expected = $"sha256={Convert.ToHexString(expectedHash).ToLowerInvariant()}";

        result.Should().Be(expected);
    }

    [Fact]
    public void ComputeSignature_StartsWithSha256Prefix()
    {
        var result = HmacSignatureGenerator.ComputeSignature("test", "secret");

        result.Should().StartWith("sha256=");
    }

    [Fact]
    public void ComputeSignature_ProducesLowercaseHex()
    {
        var result = HmacSignatureGenerator.ComputeSignature("test", "secret");
        var hexPart = result["sha256=".Length..];

        hexPart.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void ComputeSignature_DifferentSecretsProduceDifferentSignatures()
    {
        var payload = "same payload";
        var sig1 = HmacSignatureGenerator.ComputeSignature(payload, "secret1");
        var sig2 = HmacSignatureGenerator.ComputeSignature(payload, "secret2");

        sig1.Should().NotBe(sig2);
    }

    [Fact]
    public void ComputeSignature_DifferentPayloadsProduceDifferentSignatures()
    {
        var secret = "same-secret";
        var sig1 = HmacSignatureGenerator.ComputeSignature("payload1", secret);
        var sig2 = HmacSignatureGenerator.ComputeSignature("payload2", secret);

        sig1.Should().NotBe(sig2);
    }

    [Fact]
    public void ComputeSignature_SameInputProducesSameOutput()
    {
        var sig1 = HmacSignatureGenerator.ComputeSignature("test", "secret");
        var sig2 = HmacSignatureGenerator.ComputeSignature("test", "secret");

        sig1.Should().Be(sig2);
    }
}
