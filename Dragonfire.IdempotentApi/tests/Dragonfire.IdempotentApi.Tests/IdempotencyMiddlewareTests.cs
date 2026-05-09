using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Dragonfire.IdempotentApi.Attributes;
using Dragonfire.IdempotentApi.Extensions;
using Dragonfire.IdempotentApi.InMemory.Extensions;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Dragonfire.IdempotentApi.Tests;

public class IdempotencyMiddlewareTests
{
    private static IHost CreateHost(Action<WebHostBuilderContext, IServiceCollection>? extraServices = null,
                                    Action<IApplicationBuilder, WebHostBuilderContext>? configure = null)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices((ctx, services) =>
                {
                    services.AddRouting();
                    services.AddIdempotentApi(o =>
                    {
                        o.DefaultExpiration = TimeSpan.FromMinutes(5);
                    })
                    .AddAspNetCore()
                    .UseInMemoryStore();
                    extraServices?.Invoke(ctx, services);
                })
                .Configure((ctx, app) =>
                {
                    app.UseRouting();
                    app.UseIdempotentApi();

                    if (configure is not null)
                    {
                        configure(app, ctx);
                        return;
                    }

                    var counter = 0;
                    app.UseEndpoints(e =>
                    {
                        e.MapPost("/orders", async http =>
                        {
                            var id = Interlocked.Increment(ref counter);
                            http.Response.StatusCode = StatusCodes.Status201Created;
                            http.Response.ContentType = "application/json";
                            await http.Response.WriteAsync($$"""{"id":{{id}}}""");
                        });

                        e.MapGet("/orders", () => Results.Ok(new[] { "a", "b" }));
                    });
                }))
            .Start();
    }

    [Fact]
    public async Task Same_key_replays_first_response()
    {
        using var host = CreateHost();
        var client = host.GetTestClient();

        var r1 = await PostAsync(client, "k1", """{"v":1}""");
        var r2 = await PostAsync(client, "k1", """{"v":1}""");

        var b1 = await r1.Content.ReadAsStringAsync();
        var b2 = await r2.Content.ReadAsStringAsync();

        r1.StatusCode.Should().Be(HttpStatusCode.Created);
        r2.StatusCode.Should().Be(HttpStatusCode.Created);
        b2.Should().Be(b1);
        r2.Headers.GetValues("Idempotent-Replay").Should().ContainSingle().Which.Should().Be("true");
    }

    [Fact]
    public async Task Different_keys_get_independent_responses()
    {
        using var host = CreateHost();
        var client = host.GetTestClient();

        var b1 = await (await PostAsync(client, "a", """{}""")).Content.ReadAsStringAsync();
        var b2 = await (await PostAsync(client, "b", """{}""")).Content.ReadAsStringAsync();

        b1.Should().NotBe(b2);
    }

    [Fact]
    public async Task Same_key_with_different_body_returns_422()
    {
        using var host = CreateHost();
        var client = host.GetTestClient();

        var r1 = await PostAsync(client, "x", """{"v":1}""");
        var r2 = await PostAsync(client, "x", """{"v":2}""");

        r1.StatusCode.Should().Be(HttpStatusCode.Created);
        r2.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Get_request_bypasses_middleware()
    {
        using var host = CreateHost();
        var client = host.GetTestClient();

        var r = await client.GetAsync("/orders");

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        r.Headers.Contains("Idempotent-Replay").Should().BeFalse();
    }

    [Fact]
    public async Task Missing_key_with_RequireKey_returns_400()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddIdempotentApi(o =>
                    {
                        o.MissingKeyBehavior = Options.MissingKeyBehavior.RequireKey;
                    })
                    .AddAspNetCore()
                    .UseInMemoryStore();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseIdempotentApi();
                    app.UseEndpoints(e => e.MapPost("/orders", () => Results.Ok()));
                }))
            .Start();

        var client = host.GetTestClient();
        var content = new StringContent("{}", Encoding.UTF8, "application/json");

        var resp = await client.PostAsync("/orders", content);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Missing_key_with_Bypass_passes_through()
    {
        using var host = CreateHost();
        var client = host.GetTestClient();

        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var resp = await client.PostAsync("/orders", content);

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        resp.Headers.Contains("Idempotent-Replay").Should().BeFalse();
    }

    [Fact]
    public async Task Attribute_policy_skips_endpoints_without_attribute()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddIdempotentApi()
                        .AddAspNetCore(o => o.UseAttributePolicy = true)
                        .UseInMemoryStore();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseIdempotentApi();
                    var counter = 0;
                    app.UseEndpoints(e =>
                    {
                        e.MapPost("/loose", () =>
                        {
                            var id = Interlocked.Increment(ref counter);
                            return Results.Ok(new { id });
                        });
                        e.MapPost("/strict", () =>
                        {
                            var id = Interlocked.Increment(ref counter);
                            return Results.Ok(new { id });
                        }).WithMetadata(new IdempotentAttribute());
                    });
                }))
            .Start();
        var client = host.GetTestClient();

        // /loose has no [Idempotent], same key should NOT replay
        var loose1 = await PostAsync(client, "k", """{}""", "/loose");
        var loose2 = await PostAsync(client, "k", """{}""", "/loose");
        (await loose1.Content.ReadAsStringAsync()).Should().NotBe(await loose2.Content.ReadAsStringAsync());

        // /strict carries [Idempotent], same key SHOULD replay
        var strict1 = await PostAsync(client, "k2", """{}""", "/strict");
        var strict2 = await PostAsync(client, "k2", """{}""", "/strict");
        strict2.Headers.GetValues("Idempotent-Replay").Should().ContainSingle();
        (await strict1.Content.ReadAsStringAsync()).Should().Be(await strict2.Content.ReadAsStringAsync());
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string key, string body, string path = "/orders")
    {
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
        req.Headers.Add("Idempotency-Key", key);
        return client.SendAsync(req);
    }
}
