using System.Net;
using System.Net.Http;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TenantContext.AspNetCore;
using TenantContext.DependencyInjection;
using TenantContext.Resolution;
using Xunit;

namespace TenantContext.Tests;

public class AspNetCoreMiddlewareTests
{
    private static IHost Build(Action<TenantContextBuilder> configureTenant, Action<TenantResolutionOptions>? policy = null)
    {
        return new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(s =>
                {
                    var builder = s.AddTenantContext(policy);
                    configureTenant(builder);
                });
                web.Configure(app =>
                {
                    app.UseTenantContext();
                    app.Run(async ctx =>
                    {
                        var accessor = ctx.RequestServices.GetRequiredService<ITenantContextAccessor>();
                        await ctx.Response.WriteAsync(accessor.Current.IsResolved ? accessor.Current.TenantId.Value : "<none>");
                    });
                });
            })
            .Build();
    }

    [Fact]
    public async Task Header_resolver_sets_tenant_for_request()
    {
        using var host = Build(b => b.AddHeaderResolver());
        await host.StartAsync();
        var client = host.GetTestClient();
        var req = new HttpRequestMessage(HttpMethod.Get, "/");
        req.Headers.Add("X-Tenant-Id", "acme");
        var res = await client.SendAsync(req);
        (await res.Content.ReadAsStringAsync()).Should().Be("acme");
    }

    [Fact]
    public async Task No_tenant_keeps_accessor_empty()
    {
        using var host = Build(b => b.AddHeaderResolver());
        await host.StartAsync();
        var res = await host.GetTestClient().GetAsync("/");
        (await res.Content.ReadAsStringAsync()).Should().Be("<none>");
    }

    [Fact]
    public async Task Throw_policy_returns_400_when_configured_to_write_response()
    {
        using var host = Build(
            b => b.AddHeaderResolver().AddHttpOptions(o => o.WriteFailureResponse = true),
            o => o.OnMissing = MissingTenantPolicy.Throw);
        await host.StartAsync();
        var res = await host.GetTestClient().GetAsync("/");
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
