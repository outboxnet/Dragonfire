using FluentAssertions;
using Xunit;

namespace TenantContext.Tests;

public class AsyncLocalTenantContextTests
{
    [Fact]
    public void Current_is_None_by_default()
    {
        var sut = new AsyncLocalTenantContext();
        sut.Current.Should().BeSameAs(TenantInfo.None);
    }

    [Fact]
    public void BeginScope_sets_and_restores_current()
    {
        var sut = new AsyncLocalTenantContext();
        var info = new TenantInfo(new TenantId("acme"), "test");

        using (sut.BeginScope(info))
        {
            sut.Current.TenantId.Value.Should().Be("acme");
        }

        sut.Current.IsResolved.Should().BeFalse();
    }

    [Fact]
    public void Nested_scopes_restore_in_reverse_order()
    {
        var sut = new AsyncLocalTenantContext();
        var outer = new TenantInfo(new TenantId("a"), "test");
        var inner = new TenantInfo(new TenantId("b"), "test");

        using (sut.BeginScope(outer))
        {
            sut.Current.TenantId.Value.Should().Be("a");
            using (sut.BeginScope(inner))
            {
                sut.Current.TenantId.Value.Should().Be("b");
            }
            sut.Current.TenantId.Value.Should().Be("a");
        }
        sut.Current.IsResolved.Should().BeFalse();
    }

    [Fact]
    public async Task Scope_flows_across_async_awaits()
    {
        var sut = new AsyncLocalTenantContext();
        using (sut.BeginScope(new TenantInfo(new TenantId("acme"), "test")))
        {
            await Task.Yield();
            await Task.Delay(1);
            sut.Current.TenantId.Value.Should().Be("acme");
        }
    }

    [Fact]
    public async Task Parallel_flows_have_independent_state()
    {
        var sut = new AsyncLocalTenantContext();
        var results = await Task.WhenAll(Enumerable.Range(0, 50).Select(i => Task.Run(async () =>
        {
            using (sut.BeginScope(new TenantInfo(new TenantId($"t{i}"), "test")))
            {
                await Task.Yield();
                return sut.Current.TenantId.Value;
            }
        })));
        results.Should().OnlyHaveUniqueItems();
    }
}
