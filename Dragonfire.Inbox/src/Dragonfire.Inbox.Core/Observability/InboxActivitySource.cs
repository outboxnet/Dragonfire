using System.Diagnostics;

namespace Dragonfire.Inbox.Observability;

public static class InboxActivitySource
{
    public static readonly ActivitySource Source = new("Dragonfire.Inbox.Inbox", "1.0.0");
}
