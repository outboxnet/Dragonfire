using System.Diagnostics;

namespace InboxNet.Observability;

public static class InboxActivitySource
{
    public static readonly ActivitySource Source = new("InboxNet.Inbox", "1.0.0");
}
