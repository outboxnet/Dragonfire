using FluentAssertions;
using Dragonfire.TenantContext.Tasks;
using Xunit;

namespace Dragonfire.TenantContext.Tests;

public class TenantContextCaptureTests
{
    [Fact]
    public async Task Capture_then_restore_re_establishes_tenant_on_clean_thread()
    {
        var ctx = new AsyncLocalTenantContext();

        TenantContextCapture capture;
        using (ctx.BeginScope(new TenantInfo(new TenantId("acme"), "test")))
        {
            capture = new TenantContextCapture(ctx, ctx);
        }

        // After scope, ambient is empty.
        ctx.Current.IsResolved.Should().BeFalse();

        await Task.Run(() =>
        {
            ctx.Current.IsResolved.Should().BeFalse();
            using (capture.Restore())
            {
                ctx.Current.TenantId.Value.Should().Be("acme");
            }
            ctx.Current.IsResolved.Should().BeFalse();
        });
    }

    [Fact]
    public void Restore_is_noop_when_nothing_was_captured()
    {
        var ctx = new AsyncLocalTenantContext();
        var capture = new TenantContextCapture(ctx, ctx);
        using var scope = capture.Restore();
        scope.Should().NotBeNull();
        ctx.Current.IsResolved.Should().BeFalse();
    }
}
