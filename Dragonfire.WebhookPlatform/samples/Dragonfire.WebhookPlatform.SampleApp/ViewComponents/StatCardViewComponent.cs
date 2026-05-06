using Microsoft.AspNetCore.Mvc;

namespace Dragonfire.WebhookPlatform.SampleApp.ViewComponents;

/// <summary>
/// Renders one of the colored counter cards on the Overview page. Centralized so the four
/// callers stay in sync (font sizes, padding, ring color) — the only per-card difference
/// is label/value/accent.
/// </summary>
public sealed class StatCardViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(string label, int value, string accent = "orange")
        => View(new StatCardModel(label, value, accent));

    public sealed record StatCardModel(string Label, int Value, string Accent);
}
