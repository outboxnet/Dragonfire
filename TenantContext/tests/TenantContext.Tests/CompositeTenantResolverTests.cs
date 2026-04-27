using FluentAssertions;
using Microsoft.Extensions.Options;
using TenantContext.Resolution;
using Xunit;

namespace TenantContext.Tests;

public class CompositeTenantResolverTests
{
    private static IOptions<TenantResolutionOptions> Opts(Action<TenantResolutionOptions>? cfg = null)
    {
        var o = new TenantResolutionOptions();
        cfg?.Invoke(o);
        return Options.Create(o);
    }

    private static ITenantResolver Static(string name, string? id)
        => new DelegateTenantResolver(name, (_, _) =>
            id is null ? ValueTask.FromResult(TenantResolution.Unresolved)
                       : ValueTask.FromResult(TenantResolution.Resolved(new TenantId(id), name)));

    [Fact]
    public async Task Returns_first_resolved_tenant_when_short_circuiting()
    {
        var sut = new CompositeTenantResolver(
            new[] { Static("h", null), Static("c", "acme"), Static("q", "other") },
            Opts());
        var result = await sut.ResolveAsync(new TenantResolutionContext());
        result.TenantId.Value.Should().Be("acme");
        result.Source.Should().Be("c");
    }

    [Fact]
    public async Task Throws_on_missing_when_policy_says_throw()
    {
        var sut = new CompositeTenantResolver(
            new[] { Static("h", null) },
            Opts(o => o.OnMissing = MissingTenantPolicy.Throw));
        await FluentActions.Invoking(() => sut.ResolveAsync(new TenantResolutionContext()).AsTask())
            .Should().ThrowAsync<TenantResolutionException>();
    }

    [Fact]
    public async Task Uses_default_when_policy_says_default()
    {
        var sut = new CompositeTenantResolver(
            new[] { Static("h", null) },
            Opts(o =>
            {
                o.OnMissing = MissingTenantPolicy.UseDefault;
                o.DefaultTenant = new TenantId("system");
            }));
        var result = await sut.ResolveAsync(new TenantResolutionContext());
        result.TenantId.Value.Should().Be("system");
        result.Source.Should().Be("default");
    }

    [Fact]
    public async Task Throws_on_ambiguous_when_policy_says_throw()
    {
        var sut = new CompositeTenantResolver(
            new[] { Static("h", "a"), Static("c", "b") },
            Opts(o =>
            {
                o.OnAmbiguous = AmbiguityPolicy.Throw;
                o.ShortCircuitOnFirstMatch = false;
            }));
        await FluentActions.Invoking(() => sut.ResolveAsync(new TenantResolutionContext()).AsTask())
            .Should().ThrowAsync<TenantResolutionException>()
            .Where(ex => ex.Candidates.Count >= 2);
    }

    [Fact]
    public async Task Same_tenant_from_two_sources_is_not_ambiguous()
    {
        var sut = new CompositeTenantResolver(
            new[] { Static("h", "ACME"), Static("c", "acme") }, // case-insensitive equal
            Opts(o =>
            {
                o.OnAmbiguous = AmbiguityPolicy.Throw;
                o.ShortCircuitOnFirstMatch = false;
            }));
        var result = await sut.ResolveAsync(new TenantResolutionContext());
        result.TenantId.Value.Should().Be("ACME");
    }

    [Fact]
    public async Task Resolver_exception_is_wrapped()
    {
        var bad = new DelegateTenantResolver("bad", (_, _) => throw new InvalidOperationException("kaboom"));
        var sut = new CompositeTenantResolver(new[] { bad }, Opts());
        await FluentActions.Invoking(() => sut.ResolveAsync(new TenantResolutionContext()).AsTask())
            .Should().ThrowAsync<TenantResolutionException>()
            .WithInnerException<TenantResolutionException, InvalidOperationException>();
    }
}
