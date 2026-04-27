using System.Net;
using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.Options;
using TenantContext.Http;
using Xunit;

namespace TenantContext.Tests;

public class TenantPropagationHandlerTests
{
    private sealed class StubInner : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private static (HttpClient client, StubInner inner, AsyncLocalTenantContext ctx) Build(TenantPropagationOptions? opts = null)
    {
        var ctx = new AsyncLocalTenantContext();
        var inner = new StubInner();
        var handler = new TenantPropagationHandler(ctx, Options.Create(opts ?? new TenantPropagationOptions()))
        {
            InnerHandler = inner,
        };
        return (new HttpClient(handler), inner, ctx);
    }

    [Fact]
    public async Task Adds_tenant_header_when_tenant_is_ambient()
    {
        var (client, inner, ctx) = Build();
        using (ctx.BeginScope(new TenantInfo(new TenantId("acme"), "test")))
        {
            await client.GetAsync("http://example/");
        }
        inner.LastRequest!.Headers.GetValues("X-Tenant-Id").Should().ContainSingle("acme");
    }

    [Fact]
    public async Task Skips_when_no_tenant_and_policy_is_skip()
    {
        var (client, inner, _) = Build();
        await client.GetAsync("http://example/");
        inner.LastRequest!.Headers.Contains("X-Tenant-Id").Should().BeFalse();
    }

    [Fact]
    public async Task Throws_when_no_tenant_and_policy_is_throw()
    {
        var (client, _, _) = Build(new TenantPropagationOptions { OnMissing = MissingBehavior.Throw });
        await FluentActions.Invoking(() => client.GetAsync("http://example/"))
            .Should().ThrowAsync<InvalidOperationException>();
    }
}
