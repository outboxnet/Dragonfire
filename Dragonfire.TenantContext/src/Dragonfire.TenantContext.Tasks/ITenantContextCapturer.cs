using Microsoft.Extensions.DependencyInjection;
using Dragonfire.TenantContext.DependencyInjection;

namespace Dragonfire.TenantContext.Tasks;

/// <summary>Factory for <see cref="TenantContextCapture"/> snapshots. Inject into producers.</summary>
public interface ITenantContextCapturer
{
    TenantContextCapture Capture();
}

internal sealed class TenantContextCapturer : ITenantContextCapturer
{
    private readonly ITenantContextAccessor _accessor;
    private readonly ITenantContextSetter _setter;

    public TenantContextCapturer(ITenantContextAccessor accessor, ITenantContextSetter setter)
    {
        _accessor = accessor;
        _setter = setter;
    }

    public TenantContextCapture Capture() => new(_accessor, _setter);
}

public static class TenantTasksExtensions
{
    public static TenantContextBuilder AddBackgroundTaskCapture(this TenantContextBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<ITenantContextCapturer, TenantContextCapturer>();
        return builder;
    }
}
