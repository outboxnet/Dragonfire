namespace InboxNet.Providers.GitHub;

public sealed class GitHubWebhookOptions
{
    /// <summary>
    /// Shared secret configured on the GitHub webhook (Settings → Webhooks → Secret).
    /// Used to verify the <c>X-Hub-Signature-256</c> header.
    /// </summary>
    public string Secret { get; set; } = default!;

    /// <summary>
    /// Provider routing key. Defaults to <c>"github"</c>.
    /// </summary>
    public string Key { get; set; } = "github";
}
