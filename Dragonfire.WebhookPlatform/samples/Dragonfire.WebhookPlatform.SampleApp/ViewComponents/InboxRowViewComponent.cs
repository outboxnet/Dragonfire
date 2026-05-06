using Dragonfire.Inbox.Models;
using Microsoft.AspNetCore.Mvc;

namespace Dragonfire.WebhookPlatform.SampleApp.ViewComponents;

public sealed class InboxRowViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(InboxMessage message, IReadOnlyList<InboxHandlerAttempt> attempts)
        => View(new InboxRowModel(message, attempts));

    public sealed record InboxRowModel(InboxMessage Message, IReadOnlyList<InboxHandlerAttempt> Attempts);
}
