using Dragonfire.WebhookPlatform.SampleApp.Models;
using Microsoft.AspNetCore.Mvc;

namespace Dragonfire.WebhookPlatform.SampleApp.ViewComponents;

/// <summary>
/// The two-row filter bar shown above the Outgoing / Incoming lists. Posts back via GET to
/// the same URL the user is on, so the bar works for either controller without changes.
/// Showing the eventType <c>&lt;datalist&gt;</c> from <paramref name="knownEventTypes"/>
/// makes the search type-ahead style without us shipping JS.
/// </summary>
public sealed class FilterFormViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(EventFilter filter, IReadOnlyList<string> knownEventTypes)
        => View(new FilterFormModel(filter, knownEventTypes));

    public sealed record FilterFormModel(EventFilter Filter, IReadOnlyList<string> KnownEventTypes);
}
