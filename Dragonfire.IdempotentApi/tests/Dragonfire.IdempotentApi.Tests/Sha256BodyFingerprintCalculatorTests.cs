using System.Text;
using Dragonfire.IdempotentApi.Fingerprints;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Dragonfire.IdempotentApi.Tests;

public class Sha256BodyFingerprintCalculatorTests
{
    private static HttpContext CtxFor(string method, string path, string body)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = method;
        ctx.Request.Path = path;
        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return ctx;
    }

    [Fact]
    public async Task Same_method_path_body_produces_same_fingerprint()
    {
        var calc = new Sha256BodyFingerprintCalculator();

        var a = await calc.CalculateAsync(CtxFor("POST", "/orders", """{"v":1}"""), default);
        var b = await calc.CalculateAsync(CtxFor("POST", "/orders", """{"v":1}"""), default);

        a.Should().Be(b);
        a.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public async Task Different_body_produces_different_fingerprint()
    {
        var calc = new Sha256BodyFingerprintCalculator();

        var a = await calc.CalculateAsync(CtxFor("POST", "/orders", """{"v":1}"""), default);
        var b = await calc.CalculateAsync(CtxFor("POST", "/orders", """{"v":2}"""), default);

        a.Should().NotBe(b);
    }

    [Fact]
    public async Task Different_path_produces_different_fingerprint()
    {
        var calc = new Sha256BodyFingerprintCalculator();

        var a = await calc.CalculateAsync(CtxFor("POST", "/orders", "{}"), default);
        var b = await calc.CalculateAsync(CtxFor("POST", "/payments", "{}"), default);

        a.Should().NotBe(b);
    }

    [Fact]
    public async Task Body_can_be_read_again_after_fingerprinting()
    {
        var calc = new Sha256BodyFingerprintCalculator();
        var ctx = CtxFor("POST", "/orders", """{"v":1}""");

        await calc.CalculateAsync(ctx, default);

        ctx.Request.Body.Position = 0;
        using var sr = new StreamReader(ctx.Request.Body);
        var read = await sr.ReadToEndAsync();
        read.Should().Be("""{"v":1}""");
    }
}
